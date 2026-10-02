using System.Diagnostics;
using System.Runtime.Versioning;
using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.SystemIntegration;

/// <summary>IUpdateInstaller Windows 实现（docs/10 §4 P-08；S17）：Inno 安装包静默重装。
/// 参数与迁移前 UpdateService.InstallSilently 逐字一致——由安装器接管关闭应用与完成后自动重启。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateInstaller : IUpdateInstaller
{
    public void Install(string localPackagePath)
    {
        Process.Start(new ProcessStartInfo(localPackagePath)
        {
            UseShellExecute = true,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS",
        });
    }
}
