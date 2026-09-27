using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Core.Services;

/// <summary>认证状态机编排（UC-01/EX-02）：包装 ITokenProvider，
/// 状态流转 SignedOut → SigningIn → (SignedIn | ReauthRequired)；
/// 登录重入被拒绝；静默失败标记 ReauthRequired（驱动托盘提示与账户状态联动，S4+）。</summary>
public sealed class AuthService
{
    private readonly ITokenProvider _provider;
    private readonly object _gate = new();

    public AuthService(ITokenProvider provider) => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public AuthState State { get; private set; } = AuthState.SignedOut;

    public event EventHandler<AuthState>? StateChanged;

    public async Task<AuthResult> SignInAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (State == AuthState.SigningIn)
            {
                return AuthResult.Fail(AuthErrorCodes.Cancelled, "登录正在进行中"); // 重入拒绝
            }

            State = AuthState.SigningIn;
        }

        RaiseStateChanged();

        var result = await _provider.SignInInteractiveAsync(ct);

        lock (_gate)
        {
            // AUTH-003（交互流中刷新失败）→ ReauthRequired；AUTH-001 取消 / AUTH-002 需管理员批准 → SignedOut（走预案向导）
            State = result.IsSuccess
                ? AuthState.SignedIn
                : result.ErrorCode == AuthErrorCodes.ReauthRequired ? AuthState.ReauthRequired : AuthState.SignedOut;
        }

        RaiseStateChanged();
        return result;
    }

    public async Task<AuthResult> GetTokenAsync(CancellationToken ct)
    {
        var result = await _provider.AcquireTokenSilentAsync(forceRefresh: false, ct);

        lock (_gate)
        {
            if (result.IsSuccess)
            {
                if (State != AuthState.SignedIn)
                {
                    State = AuthState.SignedIn; // 恢复（重新登录后静默成功）
                }
            }
            else if (result.ErrorCode == AuthErrorCodes.ReauthRequired)
            {
                State = AuthState.ReauthRequired; // EX-02：改密/吊销 → 需重新登录
            }
        }

        RaiseStateChanged();
        return result;
    }

    public async Task SignOutAsync(CancellationToken ct)
    {
        await _provider.SignOutAsync(ct); // 清令牌缓存并撤销（FR-01 AC3）

        lock (_gate)
        {
            State = AuthState.SignedOut;
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, State);
}
