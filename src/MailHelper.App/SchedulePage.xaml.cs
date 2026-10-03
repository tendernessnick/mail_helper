using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MailHelper.ViewModels;

namespace MailHelper.App;

/// <summary>日程页（S18/CHG-015）：分组展示 Canvas 作业 DDL；操作命令走共享 ScheduleViewModel，
/// 外链仅放行 http/https（与阅读窗格外链红线一致）。</summary>
public partial class SchedulePage : UserControl
{
    public SchedulePage()
    {
        InitializeComponent();
    }

    /// <summary>进入页面即刷新（MainWindow 导航切换时调用）。</summary>
    public async Task RefreshOnEnterAsync()
    {
        if (DataContext is ScheduleViewModel vm)
        {
            await vm.RefreshAsync(CancellationToken.None);
        }
    }

    private void OnOpenLink(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url }
            && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
