using Avalonia.Controls;
using Avalonia.Interactivity;
using MailHelper.ViewModels;

namespace MailHelper.App.Avalonia;

/// <summary>Shell（05 §2 信息架构：收件箱/规则/设置 ≤2 层导航；MS3 骨架：收件箱三栏 + 导航切换；
/// 规则/设置占位页 MS7 落地完整功能。顶栏文案与 WPF 版同形态直书（行为一致红线）。</summary>
public partial class ShellWindow : Window
{
    private readonly RulesViewModel _rules;
    private readonly SettingsViewModel _settings;
    private Control? _inboxView;

    public ShellWindow(MainViewModel inbox, RulesViewModel rules, SettingsViewModel settings)
    {
        InitializeComponent();
        _rules = rules;
        _settings = settings;
        _inboxView = (Control)PageHost.Content!;
        AttachRulesContent();
        // DEV 种子数据加载（等价 WPF MainWindow.Loaded → InitializeAsync：DEV=假通道全链路）
        Loaded += async (_, _) => await inbox.InitializeAsync(CancellationToken.None);
    }

    public void ShowFromTray() // 托盘唤起（MS6 接 TrayIcon；当前为单实例唤起回调）
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private Control _rulesView = new Control();

    private void AttachRulesContent()
    {
        var page = new RulesPage { DataContext = _rules };
        _rulesView = page;
    }

    private void OnNavInbox(object? sender, RoutedEventArgs e) => SwitchNav(NavInbox, _inboxView!);

    private void OnNavRules(object? sender, RoutedEventArgs e) => SwitchNav(NavRules, _rulesView);

    private void OnNavSettings(object? sender, RoutedEventArgs e)
    {
        _settingsView ??= new SettingsPage { DataContext = _settings };
        SwitchNav(NavSettings, _settingsView);
    }

    private Control? _settingsView;

    private void SwitchNav(Button active, Control page)
    {
        NavInbox.Classes.Remove("checked");
        NavRules.Classes.Remove("checked");
        NavSettings.Classes.Remove("checked");
        active.Classes.Add("checked");
        PageHost.Content = page;
    }
}
