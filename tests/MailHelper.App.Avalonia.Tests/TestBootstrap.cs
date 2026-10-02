using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using MailHelper.App.Avalonia;
using MailHelper.Core.Abstractions;
using MailHelper.Infrastructure.SystemIntegration;
using MailHelper.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: AvaloniaTestApplication(typeof(MailHelper.App.Avalonia.Tests.TestBootstrap))]

namespace MailHelper.App.Avalonia.Tests;

/// <summary>headless 测试应用：复用产品 App 资源（Fluent + DesignTokens），无头平台；
/// OnFrameworkInitializationCompleted 的桌面生命周期分支不会被触发（HeadlessLifetime）。</summary>
public class TestBootstrap : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia(); // 帧截图（CaptureRenderedFrame）需要 Skia 渲染

    /// <summary>构建真实 DEV 主机（DevSeed 假通道 + 临时数据目录；平台锁按 OS 就位）。</summary>
    public static (IHost Host, ShellWindow Shell) BuildShell()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mh-headless-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MAILHELPER_DEV", "1");
        Environment.SetEnvironmentVariable("MAILHELPER_DATA_DIR", tempDir);
        Environment.SetEnvironmentVariable("MAILHELPER_LANG", "zh-CN");

        var suffix = Guid.NewGuid().ToString("N");
        ISingleInstanceLock singleInstanceLock = OperatingSystem.IsWindows()
            ? new WindowsSingleInstanceLock($"Local\\mh-headless-{suffix}", $"mh-headless-{suffix}")
            : new MacSingleInstanceLock(Path.Combine(tempDir, "instance.sock"));

        var host = AvaloniaBootstrapper.BuildHost(singleInstanceLock);
        host.Start();
        CategoryCatalog.RefreshAsync(host.Services.GetRequiredService<ICategoryStore>()).GetAwaiter().GetResult();

        var shell = host.Services.GetRequiredService<ShellWindow>();
        shell.DataContext = host.Services.GetRequiredService<MainViewModel>();
        return (host, shell);
    }
}
