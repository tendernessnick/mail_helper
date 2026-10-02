using System.Runtime.Versioning;
using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.SystemIntegration;

/// <summary>IAppPaths Windows 实现（docs/10 §4 P-02）：%AppData%\MailHelper（漫游）。
/// 等价迁移锚点：与迁移前 Bootstrapper.cs:31-32 / App.xaml.cs:134-135 的拼接逐字节一致；
/// overrideDataDir 承载 MAILHELPER_DATA_DIR 覆盖语义（UI 冒烟测试用独立临时目录）。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppPaths(string? overrideDataDir = null) : IAppPaths
{
    public string DataDir { get; } = overrideDataDir
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MailHelper");

    public string LogsDir => Path.Combine(DataDir, "logs");

    public string BodiesDir => Path.Combine(DataDir, "bodies");

    public string DbPath => Path.Combine(DataDir, "mailhelper.db");

    public string GetTempFilePath(string fileName) => Path.Combine(Path.GetTempPath(), fileName);
}
