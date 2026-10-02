using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.SystemIntegration;
using MailHelper.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MailHelper.App.Avalonia;

public partial class App : Application
{
    private ISingleInstanceLock? _instanceLock;
    private IHost? _host;

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

            var shell = _host.Services.GetRequiredService<ShellWindow>();
            shell.DataContext = _host.Services.GetRequiredService<MainViewModel>();
            _instanceLock.StartListening(shell.ShowFromTray);
            desktop.MainWindow = shell;

            desktop.ShutdownRequested += (_, _) =>
            {
                _instanceLock.Dispose();
                _host.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
