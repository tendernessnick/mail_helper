using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>搜索服务（FR-13 / MOD-09）：语法解析后的组合检索（真 SQLite，含 FTS trigram 与 LIKE 双路径）。</summary>
public class SearchServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-search-" + Guid.NewGuid().ToString("N"));
    private readonly MailRepository _store;
    private readonly SearchService _service;

    public SearchServiceTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(dbPath);
        new AccountRepository(dbPath).UpsertAccountAsync(new Account(
            "acc-1", "s@connect.hku.hk", "测试", "t1", ChannelKind.Graph, null,
            AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None)
            .GetAwaiter().GetResult();
        _store = new MailRepository(dbPath);
        _service = new SearchService(_store, "acc-1");
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task SeedAsync()
    {
        MailMessage M(string id, string subject, string from, MailCategory cat, Importance imp, string preview) =>
            new(id, "acc-1", $"<{id}@im>", subject, "f", from, preview, null,
                new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), false, false,
                cat, imp, 0.9, "rule", new DateTime(2026, 9, 27, 8, 0, 1, DateTimeKind.Utc), null, false);
        await _store.UpsertRangeAsync("acc-1",
        [
            M("m1", "FINAL REMINDER: Tuition Fee Payment", "bursary@hku.hk", MailCategory.Finance, Importance.P0, "Outstanding balance"),
            M("m2", "学费缴纳通知", "finance@hku.hk", MailCategory.Finance, Importance.P1, "请于截止日期前缴纳学费"),
            M("m3", "Assignment deadline", "moodle.hku.hk", MailCategory.Course, Importance.P2, "Week 4 submission"),
            M("m4", " internship invite", "careers.hku.hk", MailCategory.Career, Importance.P1, "first-round interview"),
            M("m5", "Campus maintenance", "facilities@hku.hk", MailCategory.Admin, Importance.P3, "水房维修通知"),
        ], CancellationToken.None);
    }

    [Fact]
    public async Task ChineseKeyword_LikePath_Finds()
    {
        var hits = await _service.SearchAsync("学费", 50, CancellationToken.None);

        hits.Select(m => m.Id).Should().Contain("m2"); // 「学费」双字词走 LIKE 兜底（D-21）
    }

    [Fact]
    public async Task EnglishKeyword_MatchPath_Finds()
    {
        var hits = await _service.SearchAsync("Tuition", 50, CancellationToken.None);

        hits.Select(m => m.Id).Should().Contain("m1");
    }

    [Fact]
    public async Task FromFilter_NarrowsResults()
    {
        var hits = await _service.SearchAsync("from:hku.hk payment", 50, CancellationToken.None);

        hits.Should().OnlyContain(m => m.FromAddress!.Contains("hku.hk"));
        hits.Select(m => m.Id).Should().Contain("m1"); // free text "payment" 命中 m1 正文预览
    }

    [Fact]
    public async Task CategoryFilter_Filters()
    {
        var hits = await _service.SearchAsync("cat:finance", 50, CancellationToken.None);

        hits.Select(m => m.Id).Should().BeEquivalentTo(["m1", "m2"]); // 纯过滤无文本：默认重要度→时间排序
    }

    [Fact]
    public async Task ImportanceFilter_Filters()
    {
        var hits = await _service.SearchAsync("p:P0", 50, CancellationToken.None);

        hits.Select(m => m.Id).Should().BeEquivalentTo(["m1"]);
    }

    [Fact]
    public async Task Combined_FiltersAndText()
    {
        var hits = await _service.SearchAsync("cat:finance p:P1 学费", 50, CancellationToken.None);

        hits.Select(m => m.Id).Should().BeEquivalentTo(["m2"]);
    }

    [Fact]
    public async Task BlankQuery_ReturnsEmpty()
    {
        (await _service.SearchAsync("   ", 50, CancellationToken.None)).Should().BeEmpty();
    }
}
