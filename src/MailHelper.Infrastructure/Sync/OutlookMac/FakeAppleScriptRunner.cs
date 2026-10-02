using System.Text;
using MailHelper.Core.Domain;

namespace MailHelper.Infrastructure.Sync.OutlookMac;

/// <summary>假 osascript：按入队顺序返回结果，记录全部调用（docs/10 §6.1「喂给假 osascript 验证参数与输出解析」）。
/// MS8 起驻 Infrastructure（public）——单测与「DEV 可注入 OutlookMac 假通道」共用（总控指令四.4）。</summary>
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

    /// <summary>以 DevSeed 种子数据预填 list_recent / probe_connection / get_account 响应
    /// （MS8：mac 无 Outlook 亦可 UI 冒烟，通道形状=OutlookMac；DEV 装配经 MAILHELPER_FAKE_CHANNEL=OutlookMac）。</summary>
    public static FakeAppleScriptRunner WithDevSeed()
    {
        var runner = new FakeAppleScriptRunner();
        var seed = DevSeed.BuildMailProvider();
        const char us = '\u001F';
        const char rs = '\u001E';
        var records = new List<string>();
        foreach (var page in seed.Pages)
        {
            foreach (var m in page)
            {
                var epoch = new DateTimeOffset(m.ReceivedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds();
                var sb = new StringBuilder();
                sb.Append(m.ProviderMessageId).Append(us).Append(m.Subject).Append(us)
                  .Append(m.FromName).Append(us).Append(m.FromAddress).Append(us)
                  .Append(epoch).Append(us).Append(m.HasAttachments ? "1" : "0").Append(us)
                  .Append(m.IsRead ? "1" : "0").Append(us).Append(m.BodyPreview);
                records.Add(sb.ToString());
            }
        }

        runner.Enqueue(string.Join(rs, records)); // list_recent（首次全量）
        var probe = $"1{us}dev@connect.hku.hk";
        runner.Enqueue(probe);                    // probe_connection（连接）
        runner.Enqueue(probe);                    // get_account / 后续探测
        return runner;
    }
}
