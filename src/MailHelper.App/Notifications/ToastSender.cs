using MailHelper.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.App.Notifications;

/// <summary>Toast 发送（04 §4：Microsoft.Toolkit.Uwp.Notifications 的 ToastContentBuilder）。
/// 位于 App 层的原因：Toolkit 完整 API 仅在 windows TFM 提供，而 Infrastructure 保持 net8.0 跨 TFM（D-52）。
/// 点击 launch=mailhelper://message/{id} 经 ToastNotificationManagerCompat.OnActivated 激活窗口（App 启动时监听）。
/// 未打包应用缺少开始菜单快捷方式时激活受限——发送失败记日志静默降级，不阻断同步（真实弹窗效果=检查点②）。</summary>
public sealed class ToastSender : IToastSender
{
    private readonly ILogger<ToastSender> _logger;

    public ToastSender(ILogger<ToastSender>? logger = null) =>
        _logger = logger ?? NullLogger<ToastSender>.Instance;

    public Task SendAsync(ToastNotification notification, CancellationToken ct)
    {
        try
        {
            var builder = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                .AddText(notification.Title)
                .AddText(notification.Body);
            if (notification.LaunchArgument is { Length: > 0 } launch)
            {
                builder.SetProtocolActivation(new Uri(launch)); // FR-14 AC1：点击直达对应邮件
            }

            builder.Show();
            _logger.LogInformation("toast.sent has_launch={HasLaunch}", notification.LaunchArgument is not null);
        }
        catch (Exception ex) // InvalidOperationException（无快捷方式/AUMID）等环境限制
        {
            _logger.LogWarning("toast.send_failed reason={Reason}", ex.GetType().Name);
        }

        return Task.CompletedTask;
    }
}
