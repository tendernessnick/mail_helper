using MailHelper.Infrastructure.Sync.OutlookMac;

namespace MailHelper.Mac.Tests;

/// <summary>假 osascript：按入队顺序返回结果，记录全部调用（docs/10 §6.1「喂给假 osascript 验证参数与输出解析」）。</summary>
public sealed class FakeAppleScriptRunner : IAppleScriptRunner
{
    private readonly Queue<AppleScriptOutcome> _outcomes = new();

    public sealed record AppleScriptOutcome(int ExitCode = 0, string StdOut = "", string StdErr = "", bool Timeout = false);

    public sealed record ScriptCall(string ScriptName, IReadOnlyList<string> Args, TimeSpan Timeout);

    public IReadOnlyList<ScriptCall> Calls => _calls;
    private readonly List<ScriptCall> _calls = new();

    public void Enqueue(string stdout) => _outcomes.Enqueue(new AppleScriptOutcome(StdOut: stdout));

    public void EnqueueError(int exitCode, string stderr) =>
        _outcomes.Enqueue(new AppleScriptOutcome(ExitCode: exitCode, StdErr: stderr));

    public void EnqueueTimeout() => _outcomes.Enqueue(new AppleScriptOutcome(Timeout: true));

    public Task<AppleScriptResult> RunAsync(
        string scriptName, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        _calls.Add(new ScriptCall(scriptName, args, timeout));
        if (_outcomes.Count == 0)
        {
            throw new InvalidOperationException("FakeAppleScriptRunner 未入队结果");
        }

        var outcome = _outcomes.Dequeue();
        return outcome.Timeout
            ? Task.FromException<AppleScriptResult>(new OsascriptTimeoutException(scriptName, timeout))
            : Task.FromResult(new AppleScriptResult(outcome.ExitCode, outcome.StdOut, outcome.StdErr));
    }
}
