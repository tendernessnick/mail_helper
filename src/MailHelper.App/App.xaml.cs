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
        _host = Bootstrapper.BuildHost();
        _host.Start();
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // OnExit 是同步边界；Host.Dispose 内部完成停止与清理
        _host?.Dispose();
        base.OnExit(e);
    }
}
