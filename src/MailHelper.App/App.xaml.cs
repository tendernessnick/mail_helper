using System.IO;
using System.Windows;
using MailHelper.App.Notifications;
using MailHelper.Core.Abstractions;
using MailHelper.ViewModels;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.SystemIntegration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Toolkit.Uwp.Notifications;

namespace MailHelper.App;

public partial class App : Application
{
    private IHost? _host;
    private ISingleInstanceLock? _instanceLock;

    internal static TrayIconController? Tray { get; private set; }

    /// <summary>清除本地数据（FR-03 一键清除；SettingsPage 确认后回调，App 执行并提示重启）。</summary>
    internal static Action? RequestClearLocalData { get; set; }

    private CancellationTokenSource? _periodicSyncCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
            WriteCrashLog("DispatcherUnhandled", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            WriteCrashLog("UnobservedTask", args.Exception);

        // 单实例（EX-TC-07）：二次启动唤起既有窗口后退出。
        // 抢占先于 BuildHost（等价迁移前顺序：第二实例不做任何 DB 初始化）
        _instanceLock = new WindowsSingleInstanceLock();
        if (!_instanceLock.TryAcquireFirst())
        {
            _instanceLock.NotifyRunningInstance();
            Shutdown();
            return;
        }

        _host = Bootstrapper.BuildHost(_instanceLock);
        _host.Start();
        // S17：静默升级启动安装器后经此回调关闭应用，安装器完成覆盖安装并自动重启
        _host.Services.GetRequiredService<ViewModels.SettingsViewModel>()
            .ShutdownCallback = Shutdown;
        // S14-C：类别目录先于 UI 就绪（分类栏/改判/规则编辑统一取自 CategoryCatalog）
        CategoryCatalog.RefreshAsync(
            _host.Services.GetRequiredService<ICategoryStore>()).GetAwaiter().GetResult();
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        var viewModel = (MainViewModel)MainWindow.DataContext;
        _host.Services.GetRequiredService<LanguageService>()
            .ApplyAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult(); // NFR-12

        StartPeriodicSync(); // FR-04：定时增量同步（间隔来自设置，1–60 分钟）
        RequestClearLocalData = ClearLocalDataAndPromptRestart;

        InitializeTray(viewModel);
        ListenForToastActivation(viewModel);
        WireNotifications();
        _instanceLock.StartListening(() => Dispatcher.Invoke(() => ((MainWindow)MainWindow).ShowFromTray()));

        MainWindow.Show();
    }

    private void InitializeTray(MainViewModel viewModel)
    {
        Tray = new TrayIconController(
            showMainWindow: () => ((MainWindow)MainWindow).ShowFromTray(),
            syncNow: () => _ = viewModel.SyncNowCommand.ExecuteAsync(null),
            quit: () => ((MainWindow)MainWindow).QuitFromTray());
        Tray.Initialize();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.UnreadTotal))
            {
                Tray.UpdateUnread(viewModel.UnreadTotal); // FR-14 角标计数
            }
        };
    }

    private void WireNotifications()
    {
        var host = ((App)Current).Host;
        var notifications = host.Services.GetRequiredService<NotificationService>();
        var schedule = host.Services.GetRequiredService<ScheduleService>(); // S18/CHG-015
        var coordinator = host.Services.GetRequiredService<SyncCoordinator>();
        coordinator.SyncRoundCompleted += async (_, e) =>
        {
            try
            {
                await notifications.HandleNewMailsAsync(e.NewMails, e.IsInitialRound); // 04 §8 决策（P0 逐封/P1 聚合/去重/勿扰）
            }
            catch (Exception ex)
            {
                WriteCrashLog("Notify", ex); // 通知失败不影响同步
            }

            try
            {
                await schedule.ExtractPendingAsync(); // Canvas DDL 识别（首启用回填历史）
                await schedule.CheckRemindersAsync(); // 临期提醒（同一 due 恰一次）
            }
            catch (Exception ex)
            {
                WriteCrashLog("Schedule", ex); // 日程失败不影响同步
            }
        };
    }

    /// <summary>FR-04：按设置间隔启动周期同步；Bootstrapper.PeriodicSyncChanged 触发热重启。</summary>
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
        Bootstrapper.PeriodicSyncChanged -= OnPeriodicSyncChanged;
        Bootstrapper.PeriodicSyncChanged += OnPeriodicSyncChanged;
    }

    private void OnPeriodicSyncChanged(int intervalMinutes)
    {
        _periodicSyncCts?.Cancel(); // 旧循环取消优雅退出（S4 语义），新间隔即刻生效
        StartPeriodicSync();
    }

    /// <summary>FR-03 一键清除：删除数据目录（数据库/正文缓存/日志），完成后提示重启。</summary>
    private void ClearLocalDataAndPromptRestart()
    {
        try
        {
            var dataDir = _host!.Services.GetRequiredService<IAppPaths>().DataDir;
            _host?.StopAsync().GetAwaiter().GetResult();
            if (Directory.Exists(dataDir))
            {
                Directory.Delete(dataDir, recursive: true);
            }

            MessageBox.Show("本地数据已清除。应用将退出，可随时重新启动并登录。", "MailHelper",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WriteCrashLog("ClearLocalData", ex);
        }
        finally
        {
            Shutdown();
        }
    }

    private void ListenForToastActivation(MainViewModel viewModel)
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += args =>
            {
                Dispatcher.Invoke(async () =>
                {
                    ((MainWindow)MainWindow).ShowFromTray(); // FR-14 AC1：点击直达
                    if (Uri.TryCreate(args.Argument, UriKind.Absolute, out var uri)
                        && uri.Scheme == "mailhelper"
                        && uri.Host == "message"
                        && uri.Segments is [_, var idSegment])
                    {
                        await viewModel.SelectMailByIdAsync(Uri.UnescapeDataString(idSegment), System.Threading.CancellationToken.None);
                    }
                });
            };
        }
        catch (Exception ex)
        {
            WriteCrashLog("ToastActivationHook", ex); // 未打包应用（无快捷方式）下事件源不可用：静默降级
        }
    }

    private IHost Host => _host ?? throw new InvalidOperationException("Host 尚未初始化");

    internal static void WriteCrashLog(string source, Exception? exception) =>
        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "mailhelper-crash.log"),
            $"{DateTime.UtcNow:O} {source}: {exception}");

    protected override void OnExit(ExitEventArgs e)
    {
        Tray?.Dispose();
        _instanceLock?.Dispose(); // 释放互斥并停止唤起监听（等价原 SingleInstance.StopListening）
        // OnExit 是同步边界；Host.Dispose 内部完成停止与清理
        _host?.Dispose();
        base.OnExit(e);
    }
}
