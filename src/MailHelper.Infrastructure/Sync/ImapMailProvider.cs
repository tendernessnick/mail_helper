using System.Globalization;
using System.Net.Sockets;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.Infrastructure.Sync;

/// <summary>IMAP 兜底通道（FR-02 预案 2 / 04 §4.3 / 03 §5.4）：XOAUTH2 + UIDVALIDITY/UID 水位增量，
/// 输出统一 RemoteMessage（后续分类/通知管线完全复用）。
/// 断点复用 SyncCheckpoint.delta_link 字段，编码为 imap:// URI（D-59）；
/// 不感知远端删除/移动（03 §5.4 v1.0 接受）；认证失败重试一次强刷后抛 SYNC-004（04 §5）。</summary>
public sealed class ImapMailProvider(
    ITokenProvider tokens,
    Func<IImapClientAdapter> adapterFactory,
    string accountEmail,
    ILogger<ImapMailProvider>? logger = null) : IMailProvider
{
    public const string ImapHost = "outlook.office365.com";
    public const int ImapPort = 993;
    public const string ImapScope = "https://outlook.office365.com/IMAP.AccessAsUser.All"; // 09 §2 数据最小化
    public const string BreakpointPrefix = "imap://INBOX?uidvalidity=";
    private const string FolderTag = "INBOX";

    private readonly ITokenProvider _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    private readonly Func<IImapClientAdapter> _adapterFactory = adapterFactory ?? throw new ArgumentNullException(nameof(adapterFactory));
    private readonly ILogger<ImapMailProvider> _logger = logger ?? NullLogger<ImapMailProvider>.Instance;

    private uint _uidValidity;
    private uint _lastUidThisRound;

    public ChannelKind Kind => ChannelKind.Imap;

    /// <summary>适配器生命周期由 FetchDeltaAsync 的 await using 管理；Provider 本体无可释放资源。</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async IAsyncEnumerable<RemoteMessage> FetchDeltaAsync(
        string? deltaLink, int pageSize, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var (breakpointValidity, watermark) = ParseBreakpoint(deltaLink);
        await using var adapter = _adapterFactory();
        await ConnectWithRetryAsync(adapter, ct);
        _uidValidity = await adapter.GetUidValidityAsync(ct);

        if (breakpointValidity is { } validity && validity != _uidValidity)
        {
            _logger.LogInformation("sync.imap_resync uidvalidity {Old} -> {New}", validity, _uidValidity); // SYNC-003 等价：水位作废全量
            watermark = 0;
        }

        _lastUidThisRound = watermark;
        while (true)
        {
            var summaries = await adapter.FetchSummariesAsync(_lastUidThisRound, pageSize, ct);
            if (summaries.Count == 0)
            {
                break;
            }

            foreach (var summary in summaries)
            {
                var text = await adapter.FetchTextPreviewAsync(summary.Uid, 500, ct);
                _lastUidThisRound = Math.Max(_lastUidThisRound, summary.Uid);
                yield return new RemoteMessage(
                    ProviderMessageId: $"imap:{accountEmail}:{FolderTag}:{summary.Uid}", // 04 §3.1 复合键（D-60）
                    InternetMessageId: summary.InternetMessageId?.Trim('<', '>') ?? string.Empty,
                    Subject: summary.Subject ?? string.Empty,
                    FromName: summary.FromName ?? string.Empty,
                    FromAddress: summary.FromAddress ?? string.Empty,
                    BodyPreview: text.Length > 500 ? text[..500] : text,
                    HtmlPath: null, // IMAP 通道不缓存 HTML 正文（阅读窗格回退纯文本预览）
                    ReceivedAtUtc: summary.ReceivedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow,
                    HasAttachments: summary.HasAttachments,
                    IsRead: summary.IsRead,
                    Kind: ChangeKind.Added, // IMAP 仅新增感知（03 §5.4）
                    RemoteChangeKey: null); // IMAP 无 change key（CHG-008 可空语义）
            }

            if (summaries.Count < pageSize)
            {
                break;
            }
        }
    }

    /// <summary>断点推进：uidvalidity + 本轮最大 UID（协调器整轮成功后才持久化，EX-05 语义兼容）。</summary>
    public Task<string?> CompleteAsync()
    {
        if (_uidValidity == 0)
        {
            return Task.FromResult<string?>(null); // 未执行任何拉取：无可推进断点
        }

        return Task.FromResult<string?>(EncodeBreakpoint(_uidValidity, _lastUidThisRound));
    }

    public async Task<ConnectionTestResult> TestAsync()
    {
        await using var adapter = _adapterFactory();
        try
        {
            await ConnectWithRetryAsync(adapter, CancellationToken.None);
            return new ConnectionTestResult(true);
        }
        catch (MailProviderException ex)
        {
            return new ConnectionTestResult(false, ex.ErrorCode, ex.Message);
        }
    }

    private async Task ConnectWithRetryAsync(IImapClientAdapter adapter, CancellationToken ct)
    {
        var token = await AcquireAsync(forceRefresh: false, ct);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await adapter.ConnectAsync(accountEmail, token.AccessToken, ct);
                return;
            }
            catch (MailKit.Security.AuthenticationException) when (attempt == 0)
            {
                token = await AcquireAsync(forceRefresh: true, ct); // 401 → 强刷重试一次（D-31 语义）
            }
            catch (MailKit.Security.AuthenticationException ex)
            {
                throw new MailProviderException("SYNC-004", $"IMAP 认证失败：{ex.Message}"); // 04 §5
            }
            catch (Exception ex) when (ex is SocketException or ImapProtocolException or System.IO.IOException)
            {
                throw new MailProviderException("SYNC-001", $"IMAP 连接失败：{ex.Message}");
            }
        }
    }

    private async Task<AuthToken> AcquireAsync(bool forceRefresh, CancellationToken ct)
    {
        var result = await _tokens.AcquireTokenSilentAsync(forceRefresh, ct);
        return result.IsSuccess && result.Token is { } token
            ? token
            : throw new MailProviderException("AUTH-003", result.Message ?? "需要重新登录");
    }

    /// <summary>断点编码：imap://INBOX?uidvalidity={v}&amp;lastuid={u}（复用 delta_link 字段，D-59）。</summary>
    public static string EncodeBreakpoint(uint uidValidity, uint lastUid) =>
        string.Create(CultureInfo.InvariantCulture, $"{BreakpointPrefix}{uidValidity}&lastuid={lastUid}");

    /// <summary>断点解析：非法/缺失串一律视为无断点（全量重置，安全侧）。</summary>
    internal static (uint? Validity, uint Watermark) ParseBreakpoint(string? deltaLink)
    {
        if (string.IsNullOrWhiteSpace(deltaLink) || !deltaLink.StartsWith(BreakpointPrefix, StringComparison.Ordinal))
        {
            return (null, 0);
        }

        var tail = deltaLink[BreakpointPrefix.Length..];
        var and = tail.IndexOf("&lastuid=", StringComparison.Ordinal);
        if (and <= 0
            || !uint.TryParse(tail[..and], NumberStyles.Integer, CultureInfo.InvariantCulture, out var validity)
            || !uint.TryParse(tail[(and + 9)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastUid))
        {
            return (null, 0);
        }

        return (validity, lastUid);
    }
}
