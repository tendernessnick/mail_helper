using FluentAssertions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Auth;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>认证状态机（总控指令六.S3：用假令牌提供器走通授权状态机）。</summary>
public class AuthServiceTests
{
    private static AuthToken NewToken() => new(
        "access-token-1",
        DateTimeOffset.UtcNow.AddHours(1),
        AccountIdentifier: "oid.abc@tenant",
        Email: "s123456@connect.hku.hk",
        TenantId: "tenant-1",
        Scopes: new[] { "User.Read", "Mail.Read" });

    [Fact]
    public async Task SignIn_Success_TransitionsToSignedIn()
    {
        var fake = new FakeTokenProvider { InteractiveResult = AuthResult.Ok(NewToken()) };
        var service = new AuthService(fake);

        var result = await service.SignInAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Token!.Email.Should().Be("s123456@connect.hku.hk");
        service.State.Should().Be(AuthState.SignedIn);
        fake.SignInCalls.Should().Be(1);
    }

    [Fact]
    public async Task SignIn_Cancelled_ReturnsAuth001_AndStaysSignedOut()
    {
        var fake = new FakeTokenProvider
        {
            InteractiveResult = AuthResult.Fail(AuthErrorCodes.Cancelled, "用户关闭了浏览器窗口"),
        };
        var service = new AuthService(fake);

        var result = await service.SignInAsync(CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(AuthErrorCodes.Cancelled); // AUTH-001（登录取消）
        service.State.Should().Be(AuthState.SignedOut);
    }

    [Fact]
    public async Task SignIn_AdminConsentRequired_ReturnsAuth002()
    {
        var fake = new FakeTokenProvider
        {
            InteractiveResult = AuthResult.Fail(AuthErrorCodes.AdminConsentRequired, "AADSTS65001：需要管理员批准"),
        };
        var service = new AuthService(fake);

        var result = await service.SignInAsync(CancellationToken.None);

        result.ErrorCode.Should().Be(AuthErrorCodes.AdminConsentRequired); // AUTH-002 → EX-01 预案向导入口
        service.State.Should().Be(AuthState.SignedOut);
    }

    [Fact]
    public async Task GetToken_SilentFailure_MarksReauthRequired()
    {
        var fake = new FakeTokenProvider
        {
            SilentResult = AuthResult.Fail(AuthErrorCodes.ReauthRequired, "改密后刷新令牌失效"), // EX-02 / AUTH-003
        };
        var service = new AuthService(fake);

        var result = await service.GetTokenAsync(CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        service.State.Should().Be(AuthState.ReauthRequired);
    }

    [Fact]
    public async Task GetToken_Recovery_ReturnsToSignedIn()
    {
        var fake = new FakeTokenProvider { SilentResult = AuthResult.Fail(AuthErrorCodes.ReauthRequired, "x") };
        var service = new AuthService(fake);
        await service.GetTokenAsync(CancellationToken.None);
        service.State.Should().Be(AuthState.ReauthRequired);

        fake.SilentResult = AuthResult.Ok(NewToken()); // 用户重新登录后缓存恢复
        var result = await service.GetTokenAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        service.State.Should().Be(AuthState.SignedIn);
    }

    [Fact]
    public async Task SignIn_WhileSigningIn_IsRejected()
    {
        var fake = new FakeTokenProvider
        {
            InteractiveResult = AuthResult.Ok(NewToken()),
            InteractiveDelay = TimeSpan.FromMilliseconds(300),
        };
        var service = new AuthService(fake);

        var first = Task.Run(() => service.SignInAsync(CancellationToken.None));
        // 以状态就绪为门槛（CI 慢机上 Task.Delay 墙钟时序不可靠：首个登录可能已完成后第二次才发起）
        SpinWait.SpinUntil(() => service.State == AuthState.SigningIn, TimeSpan.FromSeconds(10))
            .Should().BeTrue("第一个登录应先进入 SigningIn");
        var second = await service.SignInAsync(CancellationToken.None);

        second.IsSuccess.Should().BeFalse(); // 重入被拒绝
        (await first).IsSuccess.Should().BeTrue();
        service.State.Should().Be(AuthState.SignedIn);
        fake.SignInCalls.Should().Be(1); // 第二次未触达提供器
    }

    [Fact]
    public async Task SignOut_ClearsState_AndCallsProvider()
    {
        var fake = new FakeTokenProvider { InteractiveResult = AuthResult.Ok(NewToken()) };
        var service = new AuthService(fake);
        await service.SignInAsync(CancellationToken.None);

        await service.SignOutAsync(CancellationToken.None); // FR-01 AC3

        service.State.Should().Be(AuthState.SignedOut);
        fake.SignOutCalls.Should().Be(1);
    }

    [Fact]
    public async Task StateChanged_FiresForEveryTransition()
    {
        var fake = new FakeTokenProvider { InteractiveResult = AuthResult.Ok(NewToken()) };
        var service = new AuthService(fake);
        var observed = new List<AuthState>();
        service.StateChanged += (_, state) => observed.Add(state);

        await service.SignInAsync(CancellationToken.None);
        await service.SignOutAsync(CancellationToken.None);

        observed.Should().Equal(AuthState.SigningIn, AuthState.SignedIn, AuthState.SignedOut);
    }
}
