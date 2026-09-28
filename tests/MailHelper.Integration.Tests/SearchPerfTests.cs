using System.Diagnostics;
using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>PERF-03（NFR-03）：万级数据搜索 20 组关键词，P95 &lt; 500ms（API 计时，06 §7）。</summary>
public class SearchPerfTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-searchperf-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task TenThousandMails_KeywordSearches_P95Under500ms()
    {
        var dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(dbPath);
        var repo = new MailRepository(dbPath);
        await new AccountRepository(dbPath).UpsertAccountAsync(new Account(
            "acc-1", "s@connect.hku.hk", "perf", "t1", ChannelKind.Graph, null,
            AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
        await SeedAsync(repo);

        var service = new SearchService(repo, "acc-1");
        string[] keywords =
        [
            "学费", "deadline", "缴费", "invoice", "scholarship",
            "奖学金", "assignment", "期末", "interview", "resume",
            "图书馆", "library", "选课", "enrolment", "hostel",
            "宿舍", "insurance", "签证", "visa", "orientation",
        ];
        keywords.Length.Should().Be(20); // PERF-03 口径：20 组关键词

        var stopwatch = new Stopwatch();
        var latencies = new List<long>();
        foreach (var keyword in keywords)
        {
            stopwatch.Restart();
            var hits = await service.SearchAsync(keyword, 50, CancellationToken.None);
            stopwatch.Stop();
            latencies.Add(stopwatch.ElapsedMilliseconds);
            hits.Should().NotBeNull();
        }

        latencies.Sort();
        var p95 = latencies[(int)Math.Ceiling(0.95 * latencies.Count) - 1]; // 上取整百分位
        p95.Should().BeLessThan(500, $"20 组关键词耗时 = {string.Join(",", latencies)}ms");
    }

    /// <summary>万级造数（确定性，无随机）：批 100 upsert（PERF-03 前置数据）。</summary>
    private static async Task SeedAsync(MailRepository repo)
    {
        var subjects = new[]
        {
            "学费缴纳通知 {0}", "Assignment deadline week {0}", "Scholarship invoice {0}",
            "奖学金评审通知 {0}", "图书馆到期提醒 {0}",
        };
        var batch = new List<MailMessage>(100);
        for (var i = 0; i < 10_000; i++)
        {
            batch.Add(new MailMessage(
                $"m-{i}", "acc-1", $"<m-{i}@im>",
                string.Format(subjects[i % subjects.Length], i % 50),
                "Sender", $"sender{i % 97}@hku.hk",
                $"Preview body {i}: 学费 deadline invoice 奖学金 library 内容 {i}", null,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                false, i % 3 == 0,
                (MailCategory)(i % 7), (Importance)(i % 4), 0.9, "rule",
                new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), null, false));
            if (batch.Count == 100)
            {
                await repo.UpsertRangeAsync("acc-1", batch, CancellationToken.None);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await repo.UpsertRangeAsync("acc-1", batch, CancellationToken.None);
        }
    }
}
