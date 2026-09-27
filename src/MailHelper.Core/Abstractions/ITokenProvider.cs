using MailHelper.Core.Domain;

namespace MailHelper.Core.Abstractions;

/// <summary>令牌提供器（03 章 MOD-04 认证令牌抽象；Infrastructure 以 MSAL.NET 实现，测试用 FakeTokenProvider）。
/// scopes 由实现方固定为 User.Read + Mail.Read（02 §7；IMAP 兜底场景另加 IMAP.AccessAsUser.All，S11）。</summary>
public interface ITokenProvider
{
    /// <summary>交互登录：系统浏览器 + PKCE（09 §3）。取消→AUTH-001；需管理员批准→AUTH-002。</summary>
    Task<AuthResult> SignInInteractiveAsync(CancellationToken ct);

    /// <summary>静默获取（缓存命中或刷新；FR-01 AC2）。失败→AUTH-003（需重新登录）。
    /// forceRefresh：401 后强制绕过缓存刷新（MSAL 默认对未过期令牌直接复用，D-31）。</summary>
    Task<AuthResult> AcquireTokenSilentAsync(bool forceRefresh, CancellationToken ct);

    /// <summary>登出：清除本地令牌缓存并撤销（FR-01 AC3）。</summary>
    Task SignOutAsync(CancellationToken ct);

    /// <summary>本地是否存在已缓存账户（启动判断是否需要走 Onboarding）。</summary>
    bool HasCache { get; }
}
