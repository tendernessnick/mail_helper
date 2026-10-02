using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using MailHelper.Infrastructure.Sync;
using MailHelper.Infrastructure.SystemIntegration;
using MailHelper.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MailHelper.App.Avalonia;

public partial class App : Application
{
    private ISingleInstanceLock? _instanceLock;
    private IHost? _host;
    private TrayIcon? _trayIcon;
    private CancellationTokenSource? _periodicSyncCts;
    private bool _forceExit;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 单实例（EX-TC-07 等价语义；docs/10 §8.2）：Win=命名 Mutex+管道（预览用独立名，不与 WPF 安装版互抢）；
            // mac=UDS 文件锁（socket 落数据目录 → MAILHELPER_DATA_DIR 隔离即测试隔离）
            ISingleInstanceLock instanceLock;
            if (OperatingSystem.IsWindows())
            {
                instanceLock = new WindowsSingleInstanceLock(
                    "Local\\MailHelper-Avalonia-SingleInstance", "MailHelper-Avalonia-SingleInstance-Pipe");
            }
            else if (OperatingSystem.IsMacOS())
            {
                instanceLock = new MacSingleInstanceLock(
                    MacSingleInstanceLock.DefaultSocketPath(new MacAppPaths(
                        Environment.GetEnvironmentVariable("MAILHELPER_DATA_DIR")).DataDir));
            }
            else
            {
                throw new PlatformNotSupportedException("MailHelper 支持 Windows 与 macOS");
            }

            if (!instanceLock.TryAcquireFirst())
            {
                instanceLock.NotifyRunningInstance();
                desktop.Shutdown();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            _instanceLock = instanceLock;
            _host = AvaloniaBootstrapper.BuildHost(_instanceLock);
            _host.Start();

            _ = _host.Services.GetRequiredService<LanguageService>()
                .ApplyAsync(System.Threading.CancellationToken.None); // NFR-12（文化/日期形态）

            CategoryCatalog.RefreshAsync(
                _host.Services.GetRequiredService<ICategoryStore>()).GetAwaiter().GetResult(); // S14-C：目录先于 UI 就绪

            var shell = _host.Services.GetRequiredService<ShellWindow>();
            var viewModel = _host.Services.GetRequiredService<MainViewModel>();
            shell.DataContext = viewModel;
            _instanceLock.StartListening(shell.ShowFromTray);
            desktop.MainWindow = shell;

            // 关窗常驻（FR-14 AC3）：Hide 而非退出；退出仅经托盘菜单
            shell.Closing += (_, args) =>
            {
                if (!_forceExit)
                {
                    args.Cancel = true;
                    shell.Hide();
                }
            };

            // 托盘（05 §3.5：打开/立即同步/设置/退出；FR-14 角标计数）—— MS6
            InitializeTray(viewModel, shell);

            // 通知接线（04 §8 决策层复用；MS6 mac=OsascriptToastSender）
            WireNotifications();

            // 周期同步（FR-04）+ 设置页间隔热更新
            StartPeriodicSync();

            desktop.ShutdownRequested += (_, _) => ShutdownCore();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown; // 关窗常驻前提：仅显式退出才结束进程
        }

        base.OnFrameworkInitializationCompleted();
    }

    // —— 托盘（MS6；docs/10 §8.5）——

    private void InitializeTray(MainViewModel viewModel, ShellWindow shell)
    {
        var showItem = new NativeMenuItem("打开 MailHelper");
        showItem.Click += (_, _) => shell.ShowFromTray();

        var syncItem = new NativeMenuItem("立即同步");
        syncItem.Click += (_, _) => _ = viewModel.SyncNowCommand.ExecuteAsync(null);

        var settingsItem = new NativeMenuItem("设置…");
        settingsItem.Click += (_, _) =>
        {
            shell.ShowFromTray();
            viewModel.ShowSettingsCommand.Execute(null);
        };

        var quitItem = new NativeMenuItem("退出");
        quitItem.Click += (_, _) =>
        {
            _forceExit = true;
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(syncItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "MailHelper",
            Menu = menu,
            Icon = ComposeBadgeIcon(0),
            IsVisible = true,
        };
        var icons = new TrayIcons();
        icons.Add(_trayIcon);
        TrayIcon.SetIcons(this, icons);

        // FR-14 角标计数：未读数联动（合成计数图标）
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.UnreadTotal))
            {
                Dispatcher.UIThread.Post(() => _trayIcon.Icon = ComposeBadgeIcon(viewModel.UnreadTotal));
            }
        };
    }

    /// <summary>托盘图标合成（FR-14 角标；MS6 v1：程序化绘制零资产依赖，WPF 版语义等价）。</summary>
    private static WindowIcon ComposeBadgeIcon(int unread)
    {
        const double size = 64;
        var rtb = new RenderTargetBitmap(new PixelSize(128, 128), new Vector(192, 192));
        using (var ctx = rtb.CreateDrawingContext())
        {
            var baseBrush = new SolidColorBrush(Color.Parse("#0F6CBD")).ToImmutable();
            ctx.DrawRectangle(baseBrush, null, new RoundedRect(new Rect(2, 2, size - 4, size - 4), 14));

            var white = new SolidColorBrush(Colors.White).ToImmutable();
            var envelope = new Rect(12, 20, 40, 26);
            ctx.DrawRectangle(null, new Pen(white, 3), envelope);
            ctx.DrawLine(new Pen(white, 3), envelope.TopLeft, new Point(envelope.Center.X, envelope.Center.Y));
            ctx.DrawLine(new Pen(white, 3), envelope.TopRight, new Point(envelope.Center.X, envelope.Center.Y));

            if (unread > 0)
            {
                var label = unread > 99 ? "99+" : unread.ToString();
                var dotBrush = new SolidColorBrush(Color.Parse("#D13438")).ToImmutable();
                ctx.DrawEllipse(dotBrush, null, new Rect(36, 36, 26, 26));
                var text = new FormattedText(
                    label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 14, white);
                ctx.DrawText(text, new Point(49 - text.Width / 2, 41));
            }
        }

        return new WindowIcon(rtb);
    }

    // —— 通知接线（04 §8 决策层；docs/10 §5.2）——

    private void WireNotifications()
    {
        var notifications = _host!.Services.GetRequiredService<NotificationService>();
        var coordinator = _host.Services.GetRequiredService<SyncCoordinator>();
        coordinator.SyncRoundCompleted += async (_, e) =>
        {
            try
            {
                await notifications.HandleNewMailsAsync(e.NewMails, e.IsInitialRound, CancellationToken.None);
            }
            catch (Exception)
            {
                // 通知失败不影响同步（与 WPF App 同语义）
            }
        };
    }

    // —— 周期同步（FR-04；与 WPF App.StartPeriodicSync 同构）——

    private void StartPeriodicSync()
    {
        var settings = _host!.Services.GetRequiredService<SettingsService>();
        var coordinator = _host.Services.GetRequiredService<SyncCoordinator>();
        var interval = TimeSpan.FromMinutes(
            settings.GetSyncIntervalMinutesAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult());
        _periodicSyncCts = new CancellationTokenSource();
        var token = _periodicSyncCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await coordinator.RunPeriodicAsync(interval, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }, token);
        AvaloniaBootstrapper.PeriodicSyncChanged -= OnPeriodicSyncChanged;
        AvaloniaBootstrapper.PeriodicSyncChanged += OnPeriodicSyncChanged;
    }

    private void OnPeriodicSyncChanged(int intervalMinutes)
    {
        _periodicSyncCts?.Cancel(); // 旧循环取消优雅退出（S4 语义），新间隔即刻生效
        StartPeriodicSync();
    }

    private void ShutdownCore()
    {
        _trayIcon?.Dispose();
        _instanceLock?.Dispose();
        _periodicSyncCts?.Cancel();
        _host?.Dispose();
    }

}
