namespace MailHelper.Core.Domain;

/// <summary>邮件通道异常（04 章 §5 错误处理矩阵的载体）：
/// ErrorCode ∈ SYNC-001（网络不可达）/ SYNC-002（限流退避耗尽）/ SYNC-004（IMAP 认证失败）/
/// AUTH-003（令牌刷新失败，需重新登录）/ SYNC-002（其他 HTTP 错误，见 D-34）。</summary>
public sealed class MailProviderException : Exception
{
    public string ErrorCode { get; }

    public MailProviderException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
        => ErrorCode = errorCode;
}
