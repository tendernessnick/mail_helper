using System.Diagnostics;
using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.SystemIntegration;

/// <summary>IUpdateInstaller macOS 实现（MS9，docs/10 §9）：挂载已下载的 DMG 引导手动安装
/// （拖入 Applications；自动更新列后续）。open 调用可注入（契约测试）。</summary>
public sealed class MacUpdateInstaller : IUpdateInstaller
{
    private readonly Action<string> _open;

    public MacUpdateInstaller(Action<string>? openDmg = null) =>
        _open = openDmg ?? OpenWithFinder;

    public void Install(string localPackagePath)
    {
        if (!File.Exists(localPackagePath))
        {
            throw new FileNotFoundException("更新包不存在", localPackagePath);
        }

        _open(localPackagePath); // 挂载 DMG；用户拖入 Applications 完成安装（发布页/FAQ 引导）
    }

    private static void OpenWithFinder(string path)
    {
        var psi = new ProcessStartInfo("open")
        {
            ArgumentList = { path },
            UseShellExecute = false,
        };
        Process.Start(psi);
    }
}
