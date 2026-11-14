using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Infrastructure.Reading;
using MailHelper.ViewModels;

namespace MailHelper.App.Avalonia;

/// <summary>Shell（05 §2/§3.2；MS5 三栏主界面）：Onboarding ↔ 三栏收件箱（D-44 单窗双态，绑定共享 VM）、
/// 虚拟化邮件列表（ListBox 默认虚拟化栈）、四态、阅读窗格（净化白名单渲染 docs/10 §5.1）、改判入口（UC-06）。
/// 文案与 WPF 同形态直书（行为一致红线）。</summary>
public partial class ShellWindow : Window
{
    private readonly RulesViewModel _rules;
    private readonly SettingsViewModel _settings;
    private readonly ScheduleViewModel _schedule;
    private readonly IBodyCache _bodyCache;
    private readonly ICategoryStore _categories;
    private readonly IAutoStarter _autostart;
    private readonly MainViewModel _inbox;
    private long _readerRequest;
    private Control? _inboxView;

    /// <summary>阅读窗格正文块（净化白名单；SelectedMail 变化时重建）。</summary>
    public IReadOnlyList<SanitizedBlock> ReaderBlocks { get; private set; } = Array.Empty<SanitizedBlock>();

    public ShellWindow(MainViewModel inbox, RulesViewModel rules, SettingsViewModel settings,
        ScheduleViewModel schedule, IBodyCache bodyCache,
        ICategoryStore categories, IAutoStarter autostart)
    {
        InitializeComponent();
        _rules = rules;
        _settings = settings;
        _schedule = schedule;
        _bodyCache = bodyCache;
        _categories = categories;
        _autostart = autostart;
        _inbox = inbox;
        _inboxView = (Control)PageHost.Content!;
        AttachRulesContent();

        inbox.SelectedMailHtmlNeeded += async (_, mail) => await RenderReaderAsync(mail);
        inbox.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.IsInboxView) or nameof(MainViewModel.IsScheduleView)
                or nameof(MainViewModel.IsRulesView) or nameof(MainViewModel.IsSettingsView))
            {
                UpdateNavigation();
            }
        };
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
        Loaded += async (_, _) => await inbox.InitializeAsync(CancellationToken.None);
    }

    public void ShowFromTray() // 托盘唤起（MS6 接 TrayIcon；当前为单实例唤起回调）
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // —— 阅读窗格（docs/10 §5.1：BodyCache HTML → 净化块；无正文回落纯文本预览）——

    private async Task RenderReaderAsync(MailItemViewModel? mail)
    {
        var request = Interlocked.Increment(ref _readerRequest);
        ReaderBlocks = Array.Empty<SanitizedBlock>();
        ReaderBlocksHost.ItemsSource = ReaderBlocks;
        ReaderPlaceholderText.Text = mail is null ? "选择一封邮件查看" : "正在读取正文…";
        ReaderEmptyState.IsVisible = true;
        var blocks = new List<SanitizedBlock>();
        if (mail is not null)
        {
            string? html = null;
            if (!string.IsNullOrEmpty(mail.HtmlPath))
            {
                try
                {
                    html = await _bodyCache.ReadAsync(mail.HtmlPath, CancellationToken.None);
                }
                catch (IOException)
                {
                    html = null; // 缓存读失败回落预览（正文可再点开重试）
                }
                catch (UnauthorizedAccessException)
                {
                    html = null;
                }
            }

            blocks.AddRange(html is not null
                ? HtmlSanitizer.FromHtml(html)
                : HtmlSanitizer.FromPlainText(mail.Preview));
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (request != Interlocked.Read(ref _readerRequest))
            {
                return; // 迟到的缓存读取不能盖过后选中的邮件。
            }

            ReaderBlocks = blocks;
            ReaderEmptyState.IsVisible = mail is null;
            var host = this.FindControl<ItemsControl>("ReaderBlocksHost");
            if (host is not null)
            {
                host.ItemsSource = blocks;
            }
        });
    }

    private async void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (_inbox.IsOnboarding)
        {
            return;
        }

        var shortcutModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers == shortcutModifier && e.Key == Key.F)
        {
            _inbox.ShowInboxCommand.Execute(null);
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.KeyModifiers == shortcutModifier && e.Key == Key.R)
        {
            e.Handled = true;
            if (_inbox.SyncNowCommand.CanExecute(null))
            {
                await _inbox.SyncNowCommand.ExecuteAsync(null);
            }
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && SearchBox.IsFocused)
        {
            e.Handled = true;
            if (_inbox.SearchCommand.CanExecute(null))
            {
                await _inbox.SearchCommand.ExecuteAsync(null);
            }
        }
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None
                 && (_inbox.IsSearchMode || !string.IsNullOrEmpty(_inbox.SearchText)))
        {
            e.Handled = true;
            await _inbox.ClearSearchCommand.ExecuteAsync(null);
        }
    }

    // —— 改判（UC-06：7 类别 + 自定义，CategoryCatalog 动态；纠正成本 2 次点击）——

    private void OnShowCorrectMenu(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedMail is null)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var definition in CategoryCatalog.All)
        {
            var item = new MenuItem
            {
                Header = $"{definition.Icon} {definition.Label}",
                Tag = definition.Id,
            };
            item.Click += async (_, _) =>
            {
                if (DataContext is MainViewModel current)
                {
                    await current.ApplyCorrectionAsync(definition.Id, CancellationToken.None);
                }
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt((Control)sender!);
    }

    private void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string href })
        {
            OpenExternal(href);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void OpenWithShell(ProcessStartInfo psi) => Process.Start(psi);

    private static void OpenExternal(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return; // 仅 http/https（净化红线延伸到打开动作）
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

    // —— 视图导航（05 §2；规则/设置完整功能 MS7）——

    private Control _rulesView = new Control();
    private Control? _scheduleView;
    private Control? _settingsView;

    private void AttachRulesContent()
    {
        _rulesView = new RulesPage { DataContext = _rules };
    }

    private void OnNavInbox(object? sender, RoutedEventArgs e) => _inbox.ShowInboxCommand.Execute(null);

    private void OnNavSchedule(object? sender, RoutedEventArgs e)
    {
        _inbox.ShowScheduleCommand.Execute(null);
    }

    private void OnNavRules(object? sender, RoutedEventArgs e) => _inbox.ShowRulesCommand.Execute(null);

    private void OnNavSettings(object? sender, RoutedEventArgs e)
    {
        _inbox.ShowSettingsCommand.Execute(null);
    }

    private void UpdateNavigation()
    {
        if (_inbox.IsInboxView)
        {
            SwitchNav(NavInbox, _inboxView!);
        }
        else if (_inbox.IsScheduleView)
        {
            _scheduleView ??= new SchedulePage { DataContext = _schedule };
            if (PageHost.Content != _scheduleView)
            {
                _ = ((SchedulePage)_scheduleView).RefreshOnEnterAsync();
            }

            SwitchNav(NavSchedule, _scheduleView);
        }
        else if (_inbox.IsRulesView)
        {
            SwitchNav(NavRules, _rulesView);
        }
        else if (_inbox.IsSettingsView)
        {
            _settingsView ??= new SettingsPage(_categories, _autostart) { DataContext = _settings };
            SwitchNav(NavSettings, _settingsView);
        }
    }

    private void SwitchNav(Button active, Control page)
    {
        NavInbox.Classes.Remove("checked");
        NavSchedule.Classes.Remove("checked");
        NavRules.Classes.Remove("checked");
        NavSettings.Classes.Remove("checked");
        active.Classes.Add("checked");
        PageHost.Content = page;
    }
}
