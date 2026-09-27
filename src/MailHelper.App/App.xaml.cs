using System.IO;
using System.Windows;
using MailHelper.App.Notifications;
using MailHelper.App.ViewModels;
using MailHelper.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Toolkit.Uwp.Notifications;

namespace MailHelper.App;

public partial class App : Application
{
    private IHost? _host;

    internal static TrayIconController? Tray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
            WriteCrashLog("DispatcherUnhandled", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            WriteCrashLog("UnobservedTask", args.Exception);

        // 单实例（EX-TC-07）：二次启动唤起既有窗口后退出
        if (!SingleInstance.TryAcquireFirstInstance())
        {
            SingleInstance.NotifyRunningInstance();
            Shutdown();
            return;
        }

        _host = Bootstrapper.BuildHost();
        _host.Start();
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        var viewModel = (MainViewModel)MainWindow.DataContext;

        InitializeTray(viewModel);
        ListenForToastActivation(viewModel);
        WireNotifications();
        SingleInstance.StartListening(() => Dispatcher.Invoke(() => ((MainWindow)MainWindow).ShowFromTray()));

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
        };
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
        SingleInstance.StopListening();
        // OnExit 是同步边界；Host.Dispose 内部完成停止与清理
        _host?.Dispose();
        base.OnExit(e);
    }
}
