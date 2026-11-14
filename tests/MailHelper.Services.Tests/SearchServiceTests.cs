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
        MailMessage M(string id, string subject, string from, string cat, Importance imp, string preview) =>
            new(id, "acc-1", $"<{id}@im>", subject, "f", from, preview, null,
                new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), false, false,
                cat, imp, 0.9, "rule", new DateTime(2026, 9, 27, 8, 0, 1, DateTimeKind.Utc), null, false);
        await _store.UpsertRangeAsync("acc-1",
        [
            M("m1", "FINAL REMINDER: Tuition Fee Payment", "bursary@hku.hk", CategoryIds.Finance, Importance.P0, "Outstanding balance"),
            M("m2", "学费缴纳通知", "finance@hku.hk", CategoryIds.Finance, Importance.P1, "请于截止日期前缴纳学费"),
            M("m3", "Assignment deadline", "moodle.hku.hk", CategoryIds.Course, Importance.P2, "Week 4 submission"),
            M("m4", " internship invite", "careers.hku.hk", CategoryIds.Career, Importance.P1, "first-round interview"),
            M("m5", "Campus maintenance", "facilities@hku.hk", CategoryIds.Admin, Importance.P3, "水房维修通知"),
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

    [Theory]
    [InlineData("deadline")]
    [InlineData("学费")]
    public async Task SparseCategory_FilterRunsBeforeLimit_AndLimitIsRespected(string keyword)
    {
        var received = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var messages = Enumerable.Range(0, 60).Select(i => new MailMessage(
            $"sparse-{i:D3}", "acc-1", null, keyword, "Sender", "sender@cityu.edu.hk", keyword, null,
            received.AddMinutes(i), false, false,
            i < 4 ? CategoryIds.Finance : CategoryIds.Course, Importance.P1, 0.9, "rule", received, null, false)).ToList();
        await _store.UpsertRangeAsync("acc-1", messages, CancellationToken.None);

        var hits = await _service.SearchAsync($"cat:finance {keyword}", 2, CancellationToken.None);
        hits.Should().HaveCount(2, "过滤必须在候选窗口截断前执行，并严格遵守limit");
        hits.Should().OnlyContain(m => m.Category == CategoryIds.Finance);
        (await _service.SearchAsync(keyword, 2, CancellationToken.None)).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("学费")]
    public async Task Search_ExcludesDeleted_InMatchAndLikePaths(string keyword)
    {
        await _store.UpsertRangeAsync("acc-1", [new MailMessage(
            "deleted", "acc-1", null, keyword, "Sender", "s@hku.hk", keyword, null,
            DateTime.UtcNow, false, false, CategoryIds.Finance, Importance.P0, 0.9, "rule", DateTime.UtcNow, null, true)], CancellationToken.None);
        (await _service.SearchAsync(keyword, 50, CancellationToken.None)).Should().NotContain(m => m.Id == "deleted");
        (await _store.SearchFtsAsync("acc-1", keyword, 50, CancellationToken.None)).Should().NotContain(m => m.Id == "deleted");
    }

    [Fact]
    public async Task PureFilter_DefaultSort_UsesImportanceBeforeTime_AndCaseInsensitiveSender()
    {
        var latest = new MailMessage("latest", "acc-1", null, "low priority", "Sender", "Finance@HKU.HK", null, null,
            new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), false, false,
            CategoryIds.Finance, Importance.P3, 0.9, "rule", DateTime.UtcNow, null, false);
        await _store.UpsertRangeAsync("acc-1", [latest], CancellationToken.None);
        var hits = await _service.SearchAsync("cat:finance from:hku.hk", 50, CancellationToken.None);
        hits.Select(m => m.Id).Should().Equal("m1", "m2", "latest");
    }

    [Fact]
    public async Task Search_PreservesInboxScope_SortAndOffset()
    {
        var received = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var messages = Enumerable.Range(0, 6).Select(i => new MailMessage(
            $"scope-{i}", "acc-1", null, "exam deadline", "Sender", "sender@hku.hk", "exam", null,
            received.AddMinutes(i), false, i == 5,
            i == 4 ? CategoryIds.Career : CategoryIds.Course, Importance.P1, i == 3 ? 0.9 : 0.2, "rule", received, null, false)).ToList();
        await _store.UpsertRangeAsync("acc-1", messages, CancellationToken.None);
        var scope = new InboxQuery(Category: CategoryIds.Course, NeedsReviewOnly: true, UnreadOnly: true, Sort: InboxSortMode.Date, Offset: 1);
        var hits = await _service.SearchAsync("exam", scope, 2, CancellationToken.None);
        hits.Select(m => m.Id).Should().Equal("scope-1", "scope-0");
        (await _service.SearchAsync("cat:career exam", scope, 2, CancellationToken.None)).Should().BeEmpty("查询条件与侧栏条件取交集");
    }
}
