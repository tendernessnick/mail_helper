using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MailHelper.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
            WriteCrashLog("DispatcherUnhandled", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            WriteCrashLog("UnobservedTask", args.Exception);

        _host = Bootstrapper.BuildHost();
        _host.Start();
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    internal static void WriteCrashLog(string source, Exception? exception) =>
        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "mailhelper-crash.log"),
            $"{DateTime.UtcNow:O} {source}: {exception}");


    protected override void OnExit(ExitEventArgs e)
    {
        // OnExit 是同步边界；Host.Dispose 内部完成停止与清理（S7 托盘常驻将调整退出语义）
        _host?.Dispose();
        base.OnExit(e);
    }
}
