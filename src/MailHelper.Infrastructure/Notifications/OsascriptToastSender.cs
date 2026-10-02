using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Sync.OutlookMac;

namespace MailHelper.Infrastructure.Notifications;

/// <summary>IToastSender macOS 实现（docs/10 §5.2/§5.3）：osascript display notification。
/// v1 已知差异（获批 CHG-014）：署名=「脚本编辑器」、无点击回传；去重/聚合/勿扰由 NotificationService 决策层负责
/// （notification_log，复用不变）。发送失败静默（通知失败不影响同步，语义与 WPF ToastSender 一致）。
/// 通知权限属真机项（系统设置 → 通知 → 脚本编辑器），检查点②核验。</summary>
public sealed class OsascriptToastSender(IAppleScriptRunner runner) : IToastSender
{
    public const string ScriptNotify = "notify";

    private readonly IAppleScriptRunner _runner = runner;

    public async Task SendAsync(ToastNotification notification, CancellationToken ct)
    {
        try
        {
            // 脱敏红线：Title=主题截断由上游 NotificationService 负责；这里原样透传（04 §6）
            await _runner.RunAsync(
                ScriptNotify,
                [notification.Title, notification.Body],
                TimeSpan.FromSeconds(10),
                ct).ConfigureAwait(false);
        }
        catch (OsascriptTimeoutException)
        {
            // 静默降级（WPF 同语义）
        }
        catch (MacChannelException)
        {
            // osascript 非零退出（如通知权限关闭）：静默
        }
    }
}
