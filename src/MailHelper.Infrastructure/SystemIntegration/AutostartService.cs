using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MailHelper.Infrastructure.SystemIntegration;

/// <summary>开机自启（FR-03/US-10；TC-020）：HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// 写/删值（04 §4 指定；默认关）。runKeyPath 可注入（测试用独立子键）。仅 Windows（桌面产品目标平台）。</summary>
[SupportedOSPlatform("windows")]
public sealed class AutostartService(string runKeyPath, string valueName)
{
    private readonly string _runKeyPath = runKeyPath ?? throw new ArgumentNullException(nameof(runKeyPath));
    private readonly string _valueName = valueName ?? throw new ArgumentNullException(nameof(valueName));

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            return key?.GetValue(_valueName) is string exe && File.Exists(exe.Trim('"'));
        }
    }

    public void Enable()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法定位当前进程可执行文件");
        using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
        key.SetValue(_valueName, $"\"{exe}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }
}
