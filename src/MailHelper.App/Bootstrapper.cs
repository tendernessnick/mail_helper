using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MailHelper.App;

/// <summary>
/// 通用主机构建（总控指令四.1：Microsoft.Extensions.Hosting 做依赖注入）。
/// S0 骨架仅注册主窗口；各模块服务随 S2+ 按 03/04 章经 DI 注入。
/// </summary>
internal static class Bootstrapper
{
    public static IHost BuildHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(static services =>
            {
                services.AddSingleton<MainWindow>();
            })
            .Build();
}
