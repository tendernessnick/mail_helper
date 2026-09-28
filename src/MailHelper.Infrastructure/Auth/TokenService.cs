using System.Text;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using Microsoft.Identity.Client;

namespace MailHelper.Infrastructure.Auth;

/// <summary>MSAL.NET 令牌服务（04 章 MOD-04 / 09 章 §3）：
/// OAuth2 授权码 + PKCE、系统默认浏览器（凭据不经过应用进程）、令牌缓存经 DPAPI(CurrentUser+熵) 加密落盘、
/// 登出 = 撤销账户 + 删除缓存文件（FR-01 AC3）。
/// 说明：交互登录依赖真实浏览器与 Azure 端点，单元测试不覆盖——真实登录验证 = 检查点①（PROGRESS.md 已声明）。</summary>
public sealed class TokenService : ITokenProvider
{
    /// <summary>ClientId 配置占位符（总控指令九：检查点①之前 Graph/认证不依赖真实云端）。</summary>
    public const string PlaceholderClientId = "AZURE_CLIENT_ID";

    private static readonly string[] Scopes = { "User.Read", "Mail.Read" };

    private readonly IPublicClientApplication _app;
    private readonly string _cachePath;
    private readonly DpapiFileProtector _protector;
    private readonly bool _isPlaceholder;
    private readonly string[] _scopes;

    public TokenService(string clientId, string? cacheDirectory = null, string[]? scopes = null)
    {
        _scopes = scopes ?? Scopes; // S11：IMAP 通道传 https://outlook.office365.com/IMAP.AccessAsUser.All（04 §4.3）
        _isPlaceholder = string.IsNullOrWhiteSpace(clientId) || clientId.Trim().Equals(PlaceholderClientId, StringComparison.OrdinalIgnoreCase);

        cacheDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MailHelper", "tokens");
        _cachePath = Path.Combine(cacheDirectory, "msal.cache.bin");
        _protector = new DpapiFileProtector(Encoding.UTF8.GetBytes("MailHelper.TokenCache.v1"));

        _app = PublicClientApplicationBuilder.Create(_isPlaceholder ? PlaceholderClientId : clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount) // 多租户 common（02 §7）
            .WithRedirectUri("mailhelper://auth") // 自定义协议深链（03 §5.1；协议注册随 S12 安装器落地）
            .Build();

        // 09 §3：令牌缓存自定义序列化，写盘前经 DPAPI 加密
        _app.UserTokenCache.SetBeforeAccess(args =>
        {
            var bytes = _protector.ReadFileBytes(_cachePath);
            if (bytes is not null)
            {
                args.TokenCache.DeserializeMsalV3(bytes);
            }
        });
        _app.UserTokenCache.SetAfterAccess(args =>
        {
            if (args.HasStateChanged)
            {
                _protector.WriteFileBytes(_cachePath, args.TokenCache.SerializeMsalV3());
            }
        });
    }

    public bool HasCache => File.Exists(_cachePath);

    public async Task<AuthResult> SignInInteractiveAsync(CancellationToken ct)
    {
        if (_isPlaceholder)
        {
            return AuthResult.Fail(AuthErrorCodes.ReauthRequired, "Azure ClientId 未配置（仍为占位符），等待检查点①");
        }

        try
        {
            var result = await _app.AcquireTokenInteractive(_scopes)
                .WithUseEmbeddedWebView(false) // 系统浏览器（09 §3）
                .WithSystemWebViewOptions(new SystemWebViewOptions
                {
                    HtmlMessageSuccess = "登录成功，可以关闭此窗口返回 MailHelper。",
                    HtmlMessageError = "登录失败，请关闭此窗口后重试。",
                })
                .ExecuteAsync(ct);
            return AuthResult.Ok(Map(result));
        }
        catch (Exception ex)
        {
            return AuthResult.Fail(AuthErrorMapper.MapException(ex), ex.Message);
        }
    }

    public async Task<AuthResult> AcquireTokenSilentAsync(bool forceRefresh, CancellationToken ct)
    {
        if (_isPlaceholder)
        {
            return AuthResult.Fail(AuthErrorCodes.ReauthRequired, "Azure ClientId 未配置（仍为占位符），等待检查点①");
        }

        try
        {
            var account = (await _app.GetAccountsAsync()).FirstOrDefault();
            if (account is null)
            {
                return AuthResult.Fail(AuthErrorCodes.ReauthRequired, "无已缓存账户，需要登录");
            }

            var result = await _app.AcquireTokenSilent(_scopes, account)
                .WithForceRefresh(forceRefresh) // D-31：401 后强制刷新，绕过未过期缓存令牌
                .ExecuteAsync(ct);
            return AuthResult.Ok(Map(result));
        }
        catch (Exception ex)
        {
            return AuthResult.Fail(AuthErrorMapper.MapException(ex), ex.Message);
        }
    }

    public async Task SignOutAsync(CancellationToken ct)
    {
        foreach (var account in await _app.GetAccountsAsync())
        {
            await _app.RemoveAsync(account); // 撤销本地账户记录（缓存随回调清空）
        }

        if (File.Exists(_cachePath))
        {
            File.Delete(_cachePath); // FR-01 AC3：登出后清除本地令牌
        }
    }

    private static AuthToken Map(AuthenticationResult result) => new(
        result.AccessToken,
        result.ExpiresOn,
        result.Account?.HomeAccountId.Identifier,
        result.Account?.Username,
        result.Account?.HomeAccountId.TenantId,
        result.Scopes.ToArray());
}
