using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Infrastructure.Auth;

/// <summary>测试/调试用 ITokenProvider 假实现（总控指令六.S3：供测试用的假令牌提供器）。
/// 预设 InteractiveResult / SilentResult 返回值，记录调用计数，可注入延迟模拟并发登录。</summary>
public sealed class FakeTokenProvider : ITokenProvider
{
    public AuthResult? InteractiveResult { get; set; }

    public AuthResult? SilentResult { get; set; }

    /// <summary>forceRefresh 时的静默结果（模拟 401 后刷新得到新令牌；未设置则回落 SilentResult）。</summary>
    public AuthResult? RefreshedSilentResult { get; set; }

    public TimeSpan? InteractiveDelay { get; set; }

    public int SignInCalls { get; private set; }

    public int SilentCalls { get; private set; }

    public int ForceRefreshedCalls { get; private set; }

    public int SignOutCalls { get; private set; }

    public bool HasCache { get; set; }

    public async Task<AuthResult> SignInInteractiveAsync(CancellationToken ct)
    {
        SignInCalls++;
        if (InteractiveDelay is { } delay)
        {
            await Task.Delay(delay, ct);
        }

        return InteractiveResult ?? AuthResult.Fail(AuthErrorCodes.ReauthRequired, "FakeTokenProvider 未配置 InteractiveResult");
    }

    public Task<AuthResult> AcquireTokenSilentAsync(bool forceRefresh, CancellationToken ct)
    {
        SilentCalls++;
        if (forceRefresh)
        {
            ForceRefreshedCalls++;
            return Task.FromResult(RefreshedSilentResult ?? SilentResult
                ?? AuthResult.Fail(AuthErrorCodes.ReauthRequired, "FakeTokenProvider 未配置 SilentResult"));
        }

        return Task.FromResult(SilentResult
            ?? AuthResult.Fail(AuthErrorCodes.ReauthRequired, "FakeTokenProvider 未配置 SilentResult"));
    }

    public Task SignOutAsync(CancellationToken ct)
    {
        SignOutCalls++;
        HasCache = false;
        return Task.CompletedTask;
    }
}
