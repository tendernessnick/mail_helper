namespace MailHelper.Core.Domain;

/// <summary>
/// 连接/授权健康检查结果（04 章 §2.1 IMailProvider.TestAsync 引用但未定义 → CHG-003 最小偏差定义）。
/// ErrorCode 建议取 04 章 §5 错误码（AUTH-002/AUTH-003/SYNC-001 等）。
/// </summary>
public sealed record ConnectionTestResult(
    bool IsSuccess,
    string? ErrorCode = null,
    string? Message = null)
{
    public static ConnectionTestResult Ok() => new(true);
    public static ConnectionTestResult Fail(string errorCode, string? message = null) => new(false, errorCode, message);
}
