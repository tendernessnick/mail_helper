using System.Diagnostics;
using System.Globalization;
using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.SystemIntegration;

/// <summary>IAutoStarter macOS 实现（docs/10 §4 P-04；ADR-008 零原生依赖）：
/// ~/Library/LaunchAgents/com.mailhelper.app.plist（RunAtLoad）+ launchctl bootstrap/bootout gui/$UID。
/// plist 文本与 launchctl 调用分离（launchctl 可注入假实现）→ 契约测试全平台可跑。
/// 启用态判定=plist 存在（launchctl 状态以真机检查单核验）。</summary>
public sealed class LaunchAgentAutoStart : IAutoStarter
{
    public const string Label = "com.mailhelper.app";

    private readonly string _agentsDir;
    private readonly string _plistPath;
    private readonly string _executablePath;
    private readonly Func<string[], int> _launchctl;

    public LaunchAgentAutoStart(string executablePath, Func<string[], int>? launchctl = null)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents"),
            executablePath,
            launchctl)
    {
    }

    public LaunchAgentAutoStart(string agentsDir, string executablePath, Func<string[], int>? launchctl = null)
    {
        _agentsDir = agentsDir;
        _executablePath = executablePath;
        _plistPath = Path.Combine(agentsDir, Label + ".plist");
        _launchctl = launchctl ?? RunLaunchctlReal;
    }

    public string PlistPath => _plistPath;

    public bool IsEnabled => File.Exists(_plistPath);

    public void Enable()
    {
        Directory.CreateDirectory(_agentsDir);
        File.WriteAllText(_plistPath, BuildPlist(_executablePath));
        RunLaunchctl("bootstrap", $"gui/{ResolveUid()}", _plistPath);
    }

    public void Disable()
    {
        RunLaunchctl("bootout", $"gui/{ResolveUid()}", _plistPath);
        if (File.Exists(_plistPath))
        {
            File.Delete(_plistPath);
        }
    }

    /// <summary>plist 文本构造（LaunchAgent 标准键集；登录时 RunAtLoad 拉起）。</summary>
    public static string BuildPlist(string executablePath) =>
        string.Create(CultureInfo.InvariantCulture, $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>{Label}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{executablePath}</string>
          </array>
          <key>RunAtLoad</key>
          <true/>
        </dict>
        </plist>
        """);

    private void RunLaunchctl(params string[] args)
    {
        var exitCode = _launchctl(args);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"launchctl {args[0]} 失败（退出码 {exitCode}）");
        }
    }

    private static int RunLaunchctlReal(string[] args)
    {
        var psi = new ProcessStartInfo("launchctl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string ResolveUid()
    {
        using var process = Process.Start(new ProcessStartInfo("id", "-u")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    }
}
