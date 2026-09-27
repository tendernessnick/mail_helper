namespace MailHelper.Core.Domain;

/// <summary>认证错误码（04 章 §5 错误处理矩阵 AUTH 段；Core.Services 与 Infrastructure 共用）。</summary>
public static class AuthErrorCodes
{
    /// <summary>登录取消/窗口关闭（Information 级）。</summary>
    public const string Cancelled = "AUTH-001";

    /// <summary>租户禁止用户同意第三方应用（AADSTS500021/65001 等，EX-01/RISK-01）。</summary>
    public const string AdminConsentRequired = "AUTH-002";

    /// <summary>令牌刷新失败（改密/吊销，EX-02）→ 需重新登录。</summary>
    public const string ReauthRequired = "AUTH-003";
}

/// <summary>认证状态机（UC-01/EX-02：SignedOut → SigningIn → (SignedIn | ReauthRequired)）。</summary>
public enum AuthState
{
    SignedOut,
    SigningIn,
    SignedIn,
    ReauthRequired,
}

/// <summary>访问令牌快照（仅供进程内传递与测试断言；绝不写日志——09 §5 红线）。</summary>
public sealed record AuthToken(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string? AccountIdentifier,
    string? Email,
    string? TenantId,
    string[] Scopes);

/// <summary>认证操作结果：失败时 ErrorCode 取 AuthErrorCodes，Message 供 UI 展示（不含令牌）。</summary>
public sealed record AuthResult(bool IsSuccess, AuthToken? Token, string? ErrorCode, string? Message)
{
    public static AuthResult Ok(AuthToken token) => new(true, token, null, null);

    public static AuthResult Fail(string errorCode, string? message = null) => new(false, null, errorCode, message);
}
