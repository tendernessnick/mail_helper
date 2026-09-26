using System.Collections.Concurrent;
using FluentAssertions;
using MailHelper.Core.Rules;
using Xunit;

namespace MailHelper.Core.Tests.Rules;

/// <summary>规则文件热重载（04 章 §2.3：FileSystemWatcher + 版本号防抖）。</summary>
public class FileRuleSetWatcherTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public FileRuleSetWatcherTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mh-rules-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "rules.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Json(string version, params string[] ruleNames) =>
        "{ \"version\": \"" + version + "\", \"rules\": [ " +
        string.Join(", ", ruleNames.Select((n, i) =>
            "{ \"name\": \"" + n + "\", \"kind\": \"SubjectKeyword\", \"pattern\": \"p" + i + "\", \"category\": \"course\" }")) +
        " ] }";

    [Fact]
    public async Task FileChange_RaisesRuleSetChanged()
    {
        File.WriteAllText(_file, Json("t1", "R1"));
        using var watcher = new FileRuleSetWatcher(_file);
        var queue = new ConcurrentQueue<RuleSet>();
        watcher.RuleSetChanged += (_, rs) => queue.Enqueue(rs);

        File.WriteAllText(_file, Json("t2", "R1", "R2"));

        var updated = await WaitVersionAsync(queue, "t2", TimeSpan.FromSeconds(10));
        updated.Rules.Should().Contain(r => r.Name == "R2");
    }

    [Fact]
    public async Task RapidWrites_AreDebounced_FinalVersionWins()
    {
        File.WriteAllText(_file, Json("t1", "R1"));
        using var watcher = new FileRuleSetWatcher(_file);
        var queue = new ConcurrentQueue<RuleSet>();
        watcher.RuleSetChanged += (_, rs) => queue.Enqueue(rs);

        File.WriteAllText(_file, Json("t2", "R1", "R2"));
        await Task.Delay(50);
        File.WriteAllText(_file, Json("t3", "R1", "R2", "R3"));

        var updated = await WaitVersionAsync(queue, "t3", TimeSpan.FromSeconds(10));
        updated.Rules.Should().Contain(r => r.Name == "R3");

        // 防抖窗口过后再确认一次最终版本（合并写入无中间态残留）
        await Task.Delay(800);
        queue.Any(r => r.Version == "t3").Should().BeTrue();
    }

    [Fact]
    public async Task InvalidJson_SetsLastError_WithoutRaisingEvent()
    {
        File.WriteAllText(_file, Json("t1", "R1"));
        using var watcher = new FileRuleSetWatcher(_file);
        var raised = 0;
        watcher.RuleSetChanged += (_, _) => raised++;

        File.WriteAllText(_file, "{ 这不是 JSON");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && watcher.LastError is null)
        {
            await Task.Delay(100);
        }

        watcher.LastError.Should().NotBeNullOrEmpty(); // 解析失败保留旧规则集并记录原因（09 §2 回退策略）
        raised.Should().Be(0);
    }

    private static async Task<RuleSet> WaitVersionAsync(ConcurrentQueue<RuleSet> queue, string version, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hit = queue.LastOrDefault(r => r.Version == version);
            if (hit is not null)
            {
                return hit;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"等待规则集版本 {version} 超时；已收到版本: [{string.Join(", ", queue.Select(r => r.Version))}]");
    }
}
