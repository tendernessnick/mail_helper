using MailHelper.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MailHelper.Core.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace MailHelper.App;

public partial class MainWindow : Window
{
    private const string LayoutKey = "ui.column_widths";
    private readonly MainViewModel _viewModel;
    private readonly ISettingsStore _settings;
    private string? _pendingHtml; // CoreWebView2 完成初始化前排队的正文
    private bool _forceClose; // 托盘「退出」置位；普通关窗 = 驻留托盘（FR-14 AC3 / TC-019）

    public MainWindow(
        MainViewModel viewModel,
        ISettingsStore settings,
        RulesPage rulesPage,
        SettingsPage settingsPage,
        SchedulePage schedulePage)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;
        ScheduleHost.Content = schedulePage; // S18/CHG-015：日程页（DI 页面挂载，UserControl 带 ctor 注入）
        RulesHost.Content = rulesPage; // DI 页面挂载（UserControl 带 ctor 注入，不能在 XAML 实例化）
        SettingsHost.Content = settingsPage;

        // S18：切到日程页时进入即刷新（同步轮外的人工兜底路径）
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsScheduleView)
                && schedulePage.DataContext is ScheduleViewModel scheduleVm)
            {
                _ = scheduleVm.RefreshAsync(CancellationToken.None);
            }
        };

        // S14 修复（用户验收缺陷根因）：WebView2 光栅化比例被错固定为 200%（devicePixelRatio=2），
        // 系统 DPI 实为 166.67% → 内容渲染比控件大 20%，右缘被裁。显式对齐窗口真实 DPI。
        Loaded += async (_, _) =>
        {
            RestoreLayout();
            try
            {
                await Reader.EnsureCoreWebView2Async(); // 阅读窗格就绪（FR-12 AC2 渲染前置条件）
            }
            catch (Exception ex)
            {
                ShowReaderFallback(); // RISK-04：Runtime 缺失/损坏时不因未观察异常恶化
                App.WriteCrashLog("EnsureCoreWebView2", ex);
            }

            await viewModel.InitializeAsync(CancellationToken.None);
        };
        Closing += (_, e) =>
        {
            _ = SaveLayoutAsync(); // 布局保存不阻断退出（05 §3.3）
            if (_forceClose)
            {
                return; // 真退出（托盘菜单）
            }

            e.Cancel = true;
            Hide(); // 关窗常驻：同步与通知照常（FR-14 AC3）
            App.Tray?.ShowMinimizedHint();
        };

        _viewModel.SelectedMailHtmlNeeded += async (_, mail) => await RenderMailAsync(mail);

        // 键盘（05 §7）：↑/↓ 列表导航与 Enter 由 ListBox 原生支持；Ctrl+F 聚焦搜索；Ctrl+R 见 InputBindings
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        };

        Reader.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                ConfigureSandbox();
                if (_pendingHtml is { Length: > 0 } html)
                {
                    var readerFile = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "mailhelper-reader-current.html");
                    System.IO.File.WriteAllText(readerFile, html); // S14-B：与主路径一致的 file:// 渲染
                    Reader.CoreWebView2.Navigate(new Uri(readerFile).ToString());
                    _pendingHtml = null;
                }
            }
            else
            {
                ShowReaderFallback(); // RISK-04 降级（事件与 Task 两条失败路径都会走到）
            }
        };
        Reader.NavigationStarting += OnNavigationStarting;
    }

    /// <summary>RISK-04 最小缓解：WebView2 不可用时阅读窗格给出明确提示而非白屏（头部主题/发件人仍可见）。</summary>
    private void ShowReaderFallback()
    {
        ReaderFallback.Text = "阅读组件（WebView2 Runtime）初始化失败，邮件正文无法渲染。"
            + "请安装 Microsoft Edge WebView2 Runtime 后重启应用；正文已安全缓存在本机。";
        ReaderFallback.Visibility = Visibility.Visible;
    }

    /// <summary>改判入口（FR-11/TC-014）：弹出类别菜单，选择后走反馈闭环。</summary>
    private void OnChangeCategoryClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedMail is null)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (var definition in CategoryCatalog.All) // S14-C：内置 + 自定义动态
        {
            var item = new MenuItem { Header = $"{definition.Icon} {definition.Label}" };
            var categoryId = definition.Id;
            item.Click += (_, _) => _ = _viewModel.ApplyCorrectionAsync(categoryId, CancellationToken.None);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = sender as UIElement;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>S14-B workaround：以当前布局尺寸重设 WebView2（Width 固定→还原 NaN），
    /// 逼使控件重建渲染表面。尺寸为 0（Onboarding 态）时跳过。</summary>
    private void ForceReaderRelayout()
    {
        if (Reader.ActualWidth is not double.NaN && Reader.ActualWidth > 1)
        {
            Reader.Width = Reader.ActualWidth;
            Reader.UpdateLayout();
            Reader.Width = double.NaN;
            Reader.UpdateLayout();
        }
    }

    /// <summary>托盘/管道唤起（EX-TC-07）：恢复显示并前置。</summary>
    internal void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>托盘「退出」：置真退出标志后走 Closing 保存布局并关闭。</summary>
    internal void QuitFromTray()
    {
        _forceClose = true;
        Close();
    }

    /// <summary>WebView2 沙箱（FR-12 AC2 / SEC-03：渲染不执行任何脚本）。</summary>
    private void ConfigureSandbox()
    {
        if (Reader.CoreWebView2 is not { } core)
        {
            return;
        }

        core.Settings.IsScriptEnabled = false;               // 脚本不执行
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
    }

    /// <summary>外链拦截（09 §5：外链点击前确认——v1 一律取消导航，仅渲染正文 data: 内容）。</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var readerFileUri = new Uri(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mailhelper-reader-current.html")).ToString();
        if (e.Uri is null || e.Uri == readerFileUri)
        {
            return; // 正文渲染：仅放行注入副本自身（其余 data:/file:/http 一律拦截）
        }

        e.Cancel = true;
    }

    private async Task RenderMailAsync(MailItemViewModel? mail)
    {
        if (mail is null)
        {
            return;
        }

        try
        {
            var html = await _viewModel.BuildReaderHtmlAsync(mail, CancellationToken.None);
            if (!IsLoaded)
            {
                return;
            }

            if (Reader.CoreWebView2 is null)
            {
                _pendingHtml = html; // 初始化未完成：排队，完成后由事件处理器渲染
                return;
            }

            // S14-B：注入后副本落盘并以 file:// 导航（data: URI 实测在复合 DPI 下视口异常）
            var readerFile = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "mailhelper-reader-current.html");
            System.IO.File.WriteAllText(readerFile, html ?? string.Empty);
            Reader.CoreWebView2.Navigate(new Uri(readerFile).ToString());
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("RenderMail", ex); // 取证：阅读窗格渲染异常
        }
    }

    // S14 修复（用户验收缺陷：「放大后右侧空白」）：列宽记忆从绝对像素改为**窗口占比**——
    // 像素恢复在三栏布局中导致最大化后列宽不随窗口伸缩（右侧整片 Grid 空白）。
    private void RestoreLayout()
    {
        var saved = _settings.GetAsync(LayoutKey, CancellationToken.None).GetAwaiter().GetResult();
        if (saved is null)
        {
            return;
        }

        var parts = saved.Split(';');
        if (parts.Length == 3
            && double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var left)
            && double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var middle)
            && double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var right)
            && left > 0 && middle > 0 && right > 0
            && left + middle + right is var total && total > 0)
        {
            // 按占比恢复为 Star 宽度：任何窗口尺寸下三栏同步伸缩（MinWidth 由列定义兜底）
            LeftColumn.Width = new GridLength(left / total, GridUnitType.Star);
            MiddleColumn.Width = new GridLength(middle / total, GridUnitType.Star);
            RightColumn.Width = new GridLength(right / total, GridUnitType.Star);
        }
    }

    private async Task SaveLayoutAsync() // 05 §3.3：三栏宽度持久化（占比，窗口尺寸无关）
    {
        var total = LeftColumn.ActualWidth + MiddleColumn.ActualWidth + RightColumn.ActualWidth;
        if (total <= 0)
        {
            return;
        }

        var value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:0.000};{1:0.000};{2:0.000}",
            LeftColumn.ActualWidth / total, MiddleColumn.ActualWidth / total, RightColumn.ActualWidth / total);
        try
        {
            await _settings.SetAsync(LayoutKey, value, CancellationToken.None);
        }
        catch (IOException)
        {
            // 布局保存失败不阻断退出
        }
    }
}
