using System.Diagnostics;
using System.Runtime.Versioning;

namespace MailHelper.Infrastructure.Sync.OutlookMac;

/// <summary>IAppleScriptRunner 真实实现（macOS）：osascript 进程执行资源化脚本。
/// 脚本位于 App 目录 AppleScript/&lt;name&gt;.applescript（随发布产物分发，docs/10 §6.1）。
/// 非 macOS 平台不构造本类（装配层按 OS 分支；测试恒用假实现）。</summary>
[SupportedOSPlatform("macos")]
public sealed class OsascriptScriptRunner : IAppleScriptRunner
{
    public async Task<AppleScriptResult> RunAsync(
        string scriptName, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "AppleScript", scriptName + ".applescript");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"AppleScript 资源缺失: {scriptPath}");
        }

        var psi = new ProcessStartInfo("osascript")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(scriptPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return new AppleScriptResult(
                process.ExitCode,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new OsascriptTimeoutException(scriptName, timeout);
        }
    }
}
