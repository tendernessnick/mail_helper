using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.Infrastructure.Sync;

/// <summary>Outlook 桌面数据源抽象（测试假件替换点；真实实现走 COM 晚绑定，本机验证）。</summary>
public interface IOutlookMailSource : IDisposable
{
    /// <summary>默认账户 SMTP 地址（Outlook 登录态）。</summary>
    string GetAccountAddress();

    /// <summary>收件箱中 ReceivedTime ≥ sinceUtc 的邮件（升序、至多 take 条）；sinceUtc=null 时取全部起点。
    /// excludeAtBoundary 是已读取的同时间戳 EntryID，保证一秒内超过一页邮件时仍可完整续取。</summary>
    IReadOnlyList<OutlookMessageSummary> FetchInboxSince(
        DateTime? sinceUtc, int take, IReadOnlySet<string>? excludeAtBoundary = null);
}

/// <summary>Outlook 邮件摘要 + 正文（04 §4.1 $select 的桌面等价物）。</summary>
public sealed record OutlookMessageSummary(
    string EntryId,
    string? InternetMessageId,
    string? Subject,
    string? FromName,
    string? FromAddress,
    DateTimeOffset ReceivedAtUtc,
    bool IsRead,
    int AttachmentCount,
    string TextBody,
    string HtmlBody);

/// <summary>Outlook 桌面通道（FR-02 落地路径调整 CHG-011）：复用经典 Outlook 本机登录态读取收件箱，
/// 绕开被租户禁止的 OAuth 用户同意（检查点②实况，D-65）。断点按收件时间水位（回退 24h 晚到窗口，
/// upsert 幂等防重）；无远端删除感知（同 IMAP 限制，03 §5.4 精神）。</summary>
public sealed class OutlookDesktopMailProvider(
    IOutlookMailSource source,
    IBodyCache? bodyCache = null,
    string? cacheAccountKey = null,
    ILogger<OutlookDesktopMailProvider>? logger = null) : IMailProvider
{
    // v1 的降序截页可能已跳过历史；拒绝旧断点，在升级后完整且静默地回填一次。
    public const string BreakpointPrefix = "outlook://inbox-v2?received=";
    private static readonly TimeSpan LateArrivalWindow = TimeSpan.FromHours(24);

    private readonly IOutlookMailSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IBodyCache? _bodyCache = bodyCache;
    private readonly string _cacheAccountKey = cacheAccountKey ?? "outlook";
    private readonly ILogger<OutlookDesktopMailProvider> _logger = logger ?? NullLogger<OutlookDesktopMailProvider>.Instance;

    public ChannelKind Kind => ChannelKind.OutlookDesktop;

    public bool CanResumeFromCheckpoint(string? checkpoint)
    {
        _ = ParseBreakpoint(checkpoint, out var valid);
        return valid;
    }

    /// <summary>Outlook 登录态账户地址（连接时落库与展示）；COM 不可达时抛出，由调用方呈现。</summary>
    public Task<string?> GetAccountAddressAsync(CancellationToken ct)
        => Task.Run(() => (string?)_source.GetAccountAddress(), ct);

    /// <summary>数据源生命周期由外部宿主管理；Provider 本体无可释放资源。</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private DateTimeOffset? _watermark;

    public async IAsyncEnumerable<RemoteMessage> FetchDeltaAsync(
        string? deltaLink, int pageSize, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var since = ParseBreakpoint(deltaLink, out var breakpointValid);
        _watermark = since; // 每轮重新初始化，失败轮/旧账户的内存水位不得跳过下一轮历史数据
        if (breakpointValid)
        {
            since -= LateArrivalWindow; // 晚到旧邮件窗口：水位回退 24h，upsert 幂等吸收重叠
        }

        var htmlSaved = 0;
        var boundaryIds = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var batch = await Task.Run(
                () => _source.FetchInboxSince(since?.UtcDateTime, pageSize, boundaryIds), ct);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var item in batch)
            {
                ct.ThrowIfCancellationRequested();
                var htmlPath = await SaveBodyAsync(item, ct);
                if (htmlPath is not null)
                {
                    htmlSaved++;
                }

                var received = item.ReceivedAtUtc.UtcDateTime;
                _watermark = _watermark is { } current && current > received ? current : received;
                var preview = item.TextBody.Length > 500 ? item.TextBody[..500] : item.TextBody;
                yield return new RemoteMessage(
                    ProviderMessageId: "outlook:" + item.EntryId, // EntryID 全局唯一（04 §3.1 复合键原则）
                    InternetMessageId: item.InternetMessageId?.Trim('<', '>') ?? string.Empty,
                    Subject: item.Subject ?? string.Empty,
                    FromName: item.FromName ?? string.Empty,
                    FromAddress: item.FromAddress ?? string.Empty,
                    BodyPreview: preview,
                    HtmlPath: htmlPath,
                    ReceivedAtUtc: received,
                    HasAttachments: item.AttachmentCount > 0,
                    IsRead: item.IsRead,
                    Kind: ChangeKind.Added,
                    RemoteChangeKey: null);
            }

            if (batch.Count < pageSize)
            {
                break;
            }

            // 分页游标独立于持久水位（增量首批可能完全位于 24h 回看窗口内）。
            // 同时间戳通过 EntryID 排除已读项，不能用严格 > 时间戳，否则满页边界会丢邮件。
            var nextSince = batch.Max(m => m.ReceivedAtUtc);
            if (since != nextSince)
            {
                boundaryIds.Clear();
            }

            var previousCount = boundaryIds.Count;
            foreach (var item in batch.Where(m => m.ReceivedAtUtc == nextSince))
            {
                boundaryIds.Add(item.EntryId);
            }

            if (since == nextSince && boundaryIds.Count == previousCount)
            {
                throw new MailProviderException("SYNC-002", "Outlook 分页未前进，请重试同步。");
            }

            since = nextSince;
        }

        _logger.LogInformation("sync.outlook_batch count_watermark={Watermark:o} html_cached={HtmlCached}",
            _watermark ?? DateTimeOffset.MinValue, htmlSaved);
    }

    /// <summary>断点推进：本轮最大收件时间（协调器整轮成功后持久化，EX-05 语义兼容）。</summary>
    public Task<string?> CompleteAsync()
    {
        return _watermark is { } watermark
            ? Task.FromResult<string?>(EncodeBreakpoint(watermark)) // 字段即 DateTimeOffset
            : Task.FromResult<string?>(null);
    }

    public async Task<ConnectionTestResult> TestAsync()
    {
        try
        {
            var account = await Task.Run(_source.GetAccountAddress, CancellationToken.None);
            return new ConnectionTestResult(true, Message: "Outlook 桌面通道就绪：" + account);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, "SYNC-001", $"Outlook 不可达（请确认已安装经典版 Outlook 并登录学校账号）：{ex.Message}");
        }
    }

    private async Task<string?> SaveBodyAsync(OutlookMessageSummary item, CancellationToken ct)
    {
        if (_bodyCache is null || string.IsNullOrWhiteSpace(item.HtmlBody))
        {
            return null;
        }

        try
        {
            return await _bodyCache.SaveAsync(_cacheAccountKey, item.HtmlBody, ct);
        }
        catch (IOException)
        {
            return null; // 正文缓存失败不阻断批次（预览文本仍可用）
        }
    }

    /// <summary>v2 断点编码（复用 delta_link 字段）；v1 自动重扫一次以修复历史漏页。</summary>
    public static string EncodeBreakpoint(DateTimeOffset receivedUtc) =>
        BreakpointPrefix + receivedUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>断点解析：非法/缺失一律视为无断点（全量，安全侧，D-59 同语义）。</summary>
    internal static DateTimeOffset? ParseBreakpoint(string? deltaLink, out bool valid)
    {
        valid = false;
        if (string.IsNullOrWhiteSpace(deltaLink) || !deltaLink.StartsWith(BreakpointPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var raw = deltaLink[BreakpointPrefix.Length..];
        if (raw.Length != 16 || !DateTimeOffset.TryParseExact(
                raw, "yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return null;
        }

        valid = true;
        return parsed;
    }
}
