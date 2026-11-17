using System.Windows;
using System.Windows.Controls;

namespace MailHelper.App;

/// <summary>应用内提示与确认统一使用工作台控件，避免深色窗口弹出未主题化按钮。</summary>
internal static class WorkspaceDialog
{
    internal static MessageBoxResult Show(string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new Window
        {
            Title = title, Width = 440, MinWidth = 360, MaxWidth = 560, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow,
        };
        dialog.SetResourceReference(Control.BackgroundProperty, "BrushWindowBg");
        dialog.SetResourceReference(Control.ForegroundProperty, "BrushTextPrimary");
        var layout = new StackPanel { Margin = new Thickness(26) };
        layout.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        var content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 23,
            Margin = new Thickness(0, 0, 0, 24) };
        content.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
        layout.Children.Add(content);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
        if (buttons == MessageBoxButton.YesNo)
        {
            var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80, Margin = new Thickness(0, 0, 10, 0) };
            cancel.Click += (_, _) => dialog.Close();
            actions.Children.Add(cancel);
        }

        var confirm = new Button { Content = buttons == MessageBoxButton.YesNo ? "确认" : "知道了", MinWidth = 80,
            IsDefault = buttons != MessageBoxButton.YesNo };
        confirm.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        confirm.Click += (_, _) => { result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK; dialog.Close(); };
        actions.Children.Add(confirm);
        layout.Children.Add(actions);
        dialog.Content = layout;
        dialog.ShowDialog();
        return result;
    }
}
