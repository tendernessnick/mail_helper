using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Infrastructure.Auth;

/// <summary>测试/调试用 ITokenProvider 假实现（总控指令六.S3：供测试用的假令牌提供器）。
/// 预设 InteractiveResult / SilentResult 返回值，记录调用计数，可注入延迟模拟并发登录。</summary>
public sealed class FakeTokenProvider : ITokenProvider
{
    public AuthResult? InteractiveResult { get; set; }

    public AuthResult? SilentResult { get; set; }

    public TimeSpan? InteractiveDelay { get; set; }

    public int SignInCalls { get; private set; }

    public int SilentCalls { get; private set; }

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

    public Task<AuthResult> AcquireTokenSilentAsync(CancellationToken ct)
    {
        SilentCalls++;
        return Task.FromResult(SilentResult ?? AuthResult.Fail(AuthErrorCodes.ReauthRequired, "FakeTokenProvider 未配置 SilentResult"));
    }

    public Task SignOutAsync(CancellationToken ct)
    {
        SignOutCalls++;
        HasCache = false;
        return Task.CompletedTask;
    }
}
