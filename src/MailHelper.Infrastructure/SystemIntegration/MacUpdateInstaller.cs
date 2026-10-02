using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.SystemIntegration;

/// <summary>IUpdateInstaller Mac 占位（MS4）：真实实现=「引导下载 DMG 手动安装」随 MS9 落地（docs/10 §9，
/// 自动更新列后续）。解析即抛——Mac 侧更新动作在 MS9 前不应被触发（Avalonia 设置页 MS7 前为占位页）。</summary>
public sealed class MacUpdateInstallerPending : IUpdateInstaller
{
    public void Install(string localPackagePath) =>
        throw new PlatformNotSupportedException("Mac 更新安装随 MS9 落地（docs/10 §9：引导下载 DMG 手动安装）");
}
