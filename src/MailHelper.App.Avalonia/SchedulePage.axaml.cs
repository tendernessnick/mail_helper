using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MailHelper.ViewModels;

namespace MailHelper.App.Avalonia;

/// <summary>日程页（S18/CHG-015）：分组展示 Canvas 作业 DDL；操作命令走共享 ScheduleViewModel，
/// 外链打开与 ShellWindow 阅读窗格同策略（仅 http/https）。</summary>
public partial class SchedulePage : UserControl
{
    public SchedulePage()
    {
        InitializeComponent();
    }

    /// <summary>进入页面即刷新（切页由 ShellWindow.OnNavSchedule 调用 RefreshAsync）。</summary>
    public async Task RefreshOnEnterAsync()
    {
        if (DataContext is ScheduleViewModel vm)
        {
            await vm.RefreshAsync(CancellationToken.None);
        }
    }

    private void OnOpenLink(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
        {
            OpenExternal(url);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void OpenWithShell(ProcessStartInfo psi) => Process.Start(psi);

    private static void OpenExternal(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return; // 仅 http/https（与阅读窗格链接红线一致）
        }

        if (OperatingSystem.IsWindows())
        {
            OpenWithShell(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
        }
    }
}
