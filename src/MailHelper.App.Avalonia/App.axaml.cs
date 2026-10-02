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
            // 单实例（EX-TC-07 等价语义，MS3 预览形态）：独立于 WPF 版的互斥名称——同机并行开发期两 UI 不互抢；
            // MS4 起 Windows 沿用本实现、macOS 换 UDS（socket 落数据目录，docs/10 §8.2）
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "MailHelper（Avalonia）当前仅支持 Windows 预览；macOS 平台件随 MS4 落地（docs/10 §4）");
            }

            _instanceLock = new WindowsSingleInstanceLock(
                "Local\\MailHelper-Avalonia-SingleInstance", "MailHelper-Avalonia-SingleInstance-Pipe");
            if (!_instanceLock.TryAcquireFirst())
            {
                _instanceLock.NotifyRunningInstance();
                desktop.Shutdown();
                base.OnFrameworkInitializationCompleted();
                return;
            }

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
