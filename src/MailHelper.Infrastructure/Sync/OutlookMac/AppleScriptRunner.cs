using MailHelper.Core.Domain;

namespace MailHelper.Infrastructure.Sync.OutlookMac;

/// <summary>AppleScript 执行结果（进程退出码 + 输出；docs/10 §6.1）。</summary>
public sealed record AppleScriptResult(int ExitCode, string StdOut, string StdErr);

/// <summary>osascript 执行抽象：真实实现经 osascript 进程；测试用假实现验证参数与解析（docs/10 §6.1）。</summary>
public interface IAppleScriptRunner
{
    Task<AppleScriptResult> RunAsync(
        string scriptName, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct);
}

/// <summary>单次查询超时（runner 强制；docs/10 §6.1「超时（默认 30s/查询）」）。</summary>
public sealed class OsascriptTimeoutException(string scriptName, TimeSpan timeout)
    : Exception($"AppleScript {scriptName} 执行超时（{timeout.TotalSeconds:F0}s）");

/// <summary>通道失败（携带 MAC-0xx 错误码；docs/10 §6.5 错误映射）。</summary>
public sealed class MacChannelException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

/// <summary>连接错误码与分类（docs/10 §6.5：四类 + 兜底；用户可读文案供 UI 层透出）。</summary>
public static class MacConnectionErrorCodes
{
    public const string OutlookNotRunning = "MAC-001";
    public const string NoAccount = "MAC-002";
    public const string AutomationDenied = "MAC-003";
    public const string NewOutlookUnsupported = "MAC-004";
    public const string QueryFailed = "MAC-005";

    /// <summary>按 osascript stderr/退出码分类（osascript 脚本错误退出码为 1，错误号在 stderr 括号内）。</summary>
    public static string? Classify(int exitCode, string stderr)
    {
        if (exitCode == 0)
        {
            return null;
        }

        return stderr switch
        {
            var s when s.Contains("-1743") || s.Contains("Not authorized", StringComparison.OrdinalIgnoreCase)
                => AutomationDenied,
            var s when s.Contains("-600") || s.Contains("isn't running", StringComparison.OrdinalIgnoreCase)
                => OutlookNotRunning,
            var s when s.Contains("-1708") || s.Contains("-1709")
                       || s.Contains("doesn't understand", StringComparison.OrdinalIgnoreCase)
                => NewOutlookUnsupported,
            _ => QueryFailed,
        };
    }

    /// <summary>用户可读文案（zh-CN；docs/10 §6.5 映射表）。</summary>
    public static string Message(string errorCode) => errorCode switch
    {
        OutlookNotRunning => "请先启动 Outlook 并登录学校邮箱",
        NoAccount => "未在 Outlook 中检测到已登录账户，请先在 Outlook 登录",
        AutomationDenied => "需要授权 MailHelper 控制 Outlook：系统设置 → 隐私与安全性 → 自动化 → 勾选 Outlook",
        NewOutlookUnsupported => "检测到新版 Outlook for Mac：请在菜单栏 Outlook → 勾选「旧版 Outlook」(Legacy Outlook) 并等待重启后重试",
        _ => "连接 Outlook 异常，请重试；若持续出现请开启诊断日志",
    };
}

/// <summary>增量水位编解码（docs/10 §6.3：deltaLink 槽 = otm:1:&lt;receivedEpoch Unix 秒 UTC&gt;；
/// 版本前缀为未来演进预留；不可解析→首次全量，防御解析红线）。</summary>
public static class WatermarkCodec
{
    private const string Prefix = "otm:1:";

    public static string Encode(long epochUtcSeconds) => $"{Prefix}{epochUtcSeconds}";

    public static bool TryParse(string? deltaLink, out long epochUtcSeconds)
    {
        epochUtcSeconds = 0;
        if (deltaLink is null || !deltaLink.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return long.TryParse(deltaLink[Prefix.Length..], System.Globalization.CultureInfo.InvariantCulture, out epochUtcSeconds)
               && epochUtcSeconds > 0;
    }
}
