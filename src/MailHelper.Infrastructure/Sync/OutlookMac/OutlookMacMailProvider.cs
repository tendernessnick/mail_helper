using System.Globalization;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Infrastructure.Sync.OutlookMac;

/// <summary>Mac 唯一邮件通道（docs/10 ADR-007；CHG-014）：Outlook for Mac（经典版界面）AppleScript 直读。
/// 脚本为资源文件（Sync/AppleScript/*.applescript），本类零内联脚本字符串；经 IAppleScriptRunner 执行
/// （真实=osascript 进程，测试=假 runner）。协议：0x1E 分记录、0x1F 分字段；防御解析（畸形记录跳过、
/// 空值容忍、分隔符污染由脚本侧剥离+解析器 Split 上限兜底）。
/// 增量水位 = otm:1:&lt;epoch&gt;（docs/10 §6.3）：首轮回看 30 天，其后 lookback = 距水位 + 24h 重叠，
/// 单窗上限 7 天；配合幂等 upsert 保证 FR-04 AC2。
/// 已知差异（对照 OutlookDesktopMailProvider，docs/10 §6.2 已记录）：正文 HTML 按需拉取（fetch_body，
/// MS8 接线），同步期仅预览；字典字段名以检查点②真机核验为准。</summary>
public sealed class OutlookMacMailProvider(IAppleScriptRunner runner, string cacheAccountKey) : IMailProvider
{
    public const string ScriptProbe = "probe_connection";
    public const string ScriptListRecent = "list_recent";
    public const string ScriptFetchBody = "fetch_body";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30); // docs/10 §6.1
    private const int FullSyncLookbackHours = 24 * 30; // 首轮全量回看 30 天（FR-05 Mac 语义）
    private const int OverlapHours = 24;               // 时钟漂移/晚到容错重叠窗
    private const int MaxWindowHours = 24 * 7;         // 单次查询上限 7 天
    private const int PreviewCharLimit = 500;          // 04 §9 预览上限

    private readonly IAppleScriptRunner _runner = runner;
    private readonly string _cacheAccountKey = cacheAccountKey;
    private long? _maxSeenEpoch;

    public ChannelKind Kind => ChannelKind.OutlookMac;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // 一次性 osascript 进程，无持久资源

    public async IAsyncEnumerable<RemoteMessage> FetchDeltaAsync(
        string? deltaLink, int pageSize, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _maxSeenEpoch = null;
        var lookbackHours = WatermarkCodec.TryParse(deltaLink, out var since)
            ? Math.Clamp(
                (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(since)).TotalHours + OverlapHours,
                OverlapHours, MaxWindowHours)
            : FullSyncLookbackHours;

        var result = await _runner.RunAsync(
            ScriptListRecent,
            [lookbackHours.ToString("0", CultureInfo.InvariantCulture), pageSize.ToString(CultureInfo.InvariantCulture)],
            QueryTimeout,
            ct).ConfigureAwait(false);

        EnsureSuccess(result);

        foreach (var record in result.StdOut.Split('\u001E', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            var message = TryParseRecord(record);
            if (message is not null)
            {
                yield return message;
            }
        }
    }

    public Task<string?> CompleteAsync() =>
        Task.FromResult(_maxSeenEpoch is { } epoch ? WatermarkCodec.Encode(epoch) : null);

    public async Task<ConnectionTestResult> TestAsync()
    {
        var (errorCode, _, probe) = await ProbeAsync().ConfigureAwait(false);
        if (errorCode is not null)
        {
            return ConnectionTestResult.Fail(errorCode, MacConnectionErrorCodes.Message(errorCode));
        }

        if (probe is { AccountCount: > 0 })
        {
            return ConnectionTestResult.Ok();
        }

        return ConnectionTestResult.Fail(MacConnectionErrorCodes.NoAccount, MacConnectionErrorCodes.Message(MacConnectionErrorCodes.NoAccount));
    }

    public async Task<string?> GetAccountAddressAsync(CancellationToken ct)
    {
        var (_, _, probe) = await ProbeAsync(ct).ConfigureAwait(false);
        return probe is { AccountCount: > 0 } p && p.FirstEmail.Length > 0 ? p.FirstEmail : null;
    }

    // —— 内部 ——

    private async Task<(string? ErrorCode, string? RawError, ProbeInfo? Probe)> ProbeAsync(CancellationToken ct = default)
    {
        AppleScriptResult result;
        try
        {
            result = await _runner.RunAsync(ScriptProbe, [], QueryTimeout, ct).ConfigureAwait(false);
        }
        catch (OsascriptTimeoutException)
        {
            return (MacConnectionErrorCodes.QueryFailed, "timeout", null);
        }

        var errorCode = MacConnectionErrorCodes.Classify(result.ExitCode, result.StdErr);
        if (errorCode is not null)
        {
            return (errorCode, result.StdErr, null);
        }

        var fields = result.StdOut.Split('\u001F');
        var count = fields.Length > 0 && int.TryParse(fields[0], out var n) ? n : 0;
        var email = fields.Length > 1 ? fields[1].Trim() : string.Empty;
        return (null, null, new ProbeInfo(count, email));
    }

    private sealed record ProbeInfo(int AccountCount, string FirstEmail);

    private void EnsureSuccess(AppleScriptResult result)
    {
        var errorCode = MacConnectionErrorCodes.Classify(result.ExitCode, result.StdErr);
        if (errorCode is not null)
        {
            throw new MacChannelException(errorCode, MacConnectionErrorCodes.Message(errorCode));
        }
    }

    /// <summary>记录解析：8 字段（id/subject/fromName/fromAddress/时间/hasAttach/isRead/preview）；
    /// 字段数不足或时间不可解析→整条跳过（防御格式漂移，绝不抛原始异常）。</summary>
    private RemoteMessage? TryParseRecord(string record)
    {
        var f = record.Split('\u001F', 8);
        if (f.Length < 8)
        {
            return null;
        }

        if (!TryParseReceived(f[4], out var receivedUtc))
        {
            return null;
        }

        var id = f[0].Trim();
        if (id.Length == 0)
        {
            return null;
        }

        var epoch = receivedUtc switch
        {
            DateTimeOffset dto => dto.ToUnixTimeSeconds(),
            _ => 0L,
        };
        if (_maxSeenEpoch is null || epoch > _maxSeenEpoch)
        {
            _maxSeenEpoch = epoch;
        }

        return new RemoteMessage(
            id,
            InternetMessageId: string.Empty, // AppleScript 字典无 IMI 等价字段（检查点②复核）；去重键=ProviderMessageId
            f[1],
            f[2],
            f[3],
            TruncatePreview(f[7]),
            HtmlPath: null,
            receivedUtc?.UtcDateTime ?? DateTime.UnixEpoch, // 空时间容忍：入库但不计水位
            HasAttachments: f[5] == "1",
            IsRead: f[6] == "1",
            ChangeKind.Added);
    }

    /// <summary>时间字段：脚本产出 «class isot»（YYYYMMDDTHHMMSS，Outlook 本地时区）→ UTC；
    /// 兼容直接给 Unix 秒（测试便利）。</summary>
    private static bool TryParseReceived(string raw, out DateTimeOffset? receivedUtc)
    {
        receivedUtc = null;
        var value = raw.Trim();
        if (value.Length == 0)
        {
            return true; // 空时间容忍（预览可空同理），但该记录不计水位
        }

        if (long.TryParse(value, CultureInfo.InvariantCulture, out var epoch))
        {
            receivedUtc = DateTimeOffset.FromUnixTimeSeconds(epoch);
            return true;
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var local))
        {
            receivedUtc = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            return true;
        }

        return false;
    }

    private static string TruncatePreview(string preview) =>
        preview.Length <= PreviewCharLimit ? preview : preview[..PreviewCharLimit];
}
