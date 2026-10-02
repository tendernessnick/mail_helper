using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.App.Avalonia;

/// <summary>Avalonia Windows 预览期的通知空实现（MS6）：正式 Windows 用户走 WPF 版（WinRT Toast）；
/// mac 侧为 OsascriptToastSender（docs/10 §5.2）。</summary>
public sealed class NoopToastSender : IToastSender
{
    public Task SendAsync(ToastNotification notification, CancellationToken ct) => Task.CompletedTask;
}
