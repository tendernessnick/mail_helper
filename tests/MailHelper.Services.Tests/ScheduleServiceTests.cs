using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>日程服务（S18/CHG-015）：摘要解析入库、扫描置位不重扫、同作业 due 变更更新、
/// 回填历史（GetDdlCandidates 升序即回填）、临期提醒恰一次/开关/窗口外静默、总开关短路。
/// 真实 SQLite（messages/schedule_items/body 缓存）+ FakeSettingsStore/FakeToastSender/FixedTimeProvider。</summary>
public class ScheduleServiceTests : IDisposable
{
    private static readonly DateTime NowUtc = new(2026, 10, 4, 4, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-sched-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly MailRepository _mails;
    private readonly ScheduleRepository _schedule;
    private readonly BodyCacheStore _bodyCache;
    private readonly FakeSettingsStore _settings = new();
    private readonly FakeToastSender _toasts = new();

    public ScheduleServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(_dbPath);
        // FK 链：messages.account_id → accounts（生产顺序：账户→邮件→日程）
        new AccountRepository(_dbPath).UpsertAccountAsync(new Account(
            "acc-1", "s@connect.hku.hk", "测试", "t1", ChannelKind.Graph, null,
            AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None)
            .GetAwaiter().GetResult();
        _mails = new MailRepository(_dbPath);
        _schedule = new ScheduleRepository(_dbPath);
        _bodyCache = new BodyCacheStore(Path.Combine(_dir, "bodies"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private ScheduleService NewService() => new(
        _mails, _schedule, _bodyCache, _settings, _toasts, "acc-1",
        NullLogger<ScheduleService>.Instance, new FixedTimeProvider(NowUtc));

    private async Task SeedCanvasMailAsync(string id, string bodyText, bool useBodyCache = true)
    {
        string? bodyPath = null;
        if (useBodyCache)
        {
            // 逐行 <p> + 转义：贴近真实 Canvas HTML（&lt;URL&gt; 解码后为纯文本，HtmlToText 可提取）
            var html = string.Join("", bodyText.Split('\n')
                .Select(line => $"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>"));
            bodyPath = await _bodyCache.SaveAsync("acc-1", html, CancellationToken.None);
        }

        var mail = new MailMessage(
            id, "acc-1", $"<{id}@im>", "Recent Canvas Notifications", "Canvas",
            "no-reply@canvas.cityu.edu.hk", bodyText[..Math.Min(200, bodyText.Length)], bodyPath,
            new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc), false, false,
            CategoryIds.Course, Importance.P2, 0.9, "rule", null, null, false);
        await _mails.UpsertRangeAsync("acc-1", [mail], CancellationToken.None);
    }

    private const string DigestHtmlBody =
        "Assignment Created - Week 5 assignment, IS6400 Business Data Analytics\n" +
        "due: Oct 9 at 11:59pm\n" +
        "Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/187001>\n" +
        "Assignment Created - Week 5 Monday In-class Quiz, IS6400 Business Data Analytics\n" +
        "due: Sep 28 at 5pm\n" +
        "Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/187002>\n";

    [Fact]
    public async Task Extract_Digest_InsertsSortedAndMarksScanned()
    {
        await SeedCanvasMailAsync("digest-1", DigestHtmlBody);
        var service = NewService();

        var summary = await service.ExtractPendingAsync(ct: CancellationToken.None);

        summary.Scanned.Should().Be(1);
        summary.Found.Should().Be(2);
        summary.Inserted.Should().Be(2);
        summary.UpdatedDue.Should().Be(0);

        var items = await service.GetUpcomingAsync(CancellationToken.None);
        items.Should().HaveCount(2);
        items[0].Title.Should().Be("Week 5 Monday In-class Quiz"); // due 升序：Sep 28 在前
        items[1].Title.Should().Be("Week 5 assignment");
        items[0].Status.Should().Be(ScheduleItemStatus.Open);
        items[0].Source.Should().Be("canvas");
        items[0].MessageId.Should().Be("digest-1");

        var again = await service.ExtractPendingAsync(ct: CancellationToken.None);
        again.Scanned.Should().Be(0); // 扫过即置位，不重扫（AC5）
        again.Inserted.Should().Be(0);
    }

    [Fact]
    public async Task Extract_SameAssignmentNewDue_UpdatesNotDuplicates()
    {
        await SeedCanvasMailAsync("digest-1", DigestHtmlBody);
        var service = NewService();
        await service.ExtractPendingAsync(ct: CancellationToken.None);

        // Canvas 延期：新邮件同链接、新截止时间（US-18-2 / AC3）
        await SeedCanvasMailAsync("digest-2",
            "Assignment Created - Week 5 assignment, IS6400 Business Data Analytics\n" +
            "due: Oct 12 at 5pm\n" +
            "Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/187001>");
        var summary = await service.ExtractPendingAsync(ct: CancellationToken.None);

        summary.UpdatedDue.Should().Be(1);
        summary.Inserted.Should().Be(0); // 同作业不重复建条

        var items = await service.GetUpcomingAsync(CancellationToken.None);
        items.Should().HaveCount(2);
        var updated = items.Single(i => i.DedupeKey == "canvas-a:71457:187001");
        updated.DueAtUtc.Should().Be(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 12, 17, 0, 0)));
        updated.RemindedDueAt.Should().BeNull(); // 变更后恢复提醒资格
        updated.MessageId.Should().Be("digest-2"); // 来源指向最近一次提及
    }

    [Fact]
    public async Task Extract_BodyCacheMiss_FallsBackToPreview()
    {
        await SeedCanvasMailAsync("preview-only",
            "Assignment Created - Week 4 assignment, IS6400 Business Data Analytics\n" +
            "due: Oct 2 at 11:59pm\n" +
            "Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/186500>",
            useBodyCache: false);
        var service = NewService();

        var summary = await service.ExtractPendingAsync(ct: CancellationToken.None);

        summary.Inserted.Should().Be(1); // 预览回退（单封通知 500 字符内足够）
    }

    [Fact]
    public async Task Extract_AnchorHrefHtml_CapturesCanvasLink()
    {
        // D-72 回归：真实 Canvas 邮件 URL 仅在 <a href> 属性（文本仅 "Click to view"）；
        // 不展开则链接丢失 → 去重键退化为标题哈希，due 变更会误建新条
        var html = "<p>Assignment Created - Week 5 assignment, IS6400 Business Data Analytics</p>"
            + "<p>due: Oct 9 at 11:59pm</p>"
            + "<p><a href=\"https://canvas.cityu.edu.hk/courses/71457/assignments/187001\">Click to view</a></p>";
        var bodyPath = await _bodyCache.SaveAsync("acc-1", html, CancellationToken.None);
        var mail = new MailMessage(
            "anchor-1", "acc-1", "<anchor-1@im>", "Recent Canvas Notifications", "Canvas",
            "no-reply@canvas.cityu.edu.hk", "digest", bodyPath,
            new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc), false, false,
            CategoryIds.Course, Importance.P2, 0.9, "rule", null, null, false);
        await _mails.UpsertRangeAsync("acc-1", [mail], CancellationToken.None);
        var service = NewService();

        var summary = await service.ExtractPendingAsync(ct: CancellationToken.None);

        summary.Inserted.Should().Be(1);
        var item = (await service.GetUpcomingAsync(CancellationToken.None)).Single();
        item.Link.Should().Be("https://canvas.cityu.edu.hk/courses/71457/assignments/187001");
        item.DedupeKey.Should().Be("canvas-a:71457:187001"); // 链接复合 id（AC3 稳定去重前提）
    }

    [Fact]
    public async Task Extract_NonCanvasMail_Skipped()
    {
        var mail = new MailMessage(
            "plain-1", "acc-1", "<plain-1@im>", "Assignment due tomorrow", "教授",
            "prof@cityu.edu.hk", "正文", null,
            new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc), false, false,
            CategoryIds.Course, Importance.P2, 0.9, "rule", null, null, false);
        await _mails.UpsertRangeAsync("acc-1", [mail], CancellationToken.None);

        var summary = await NewService().ExtractPendingAsync(ct: CancellationToken.None);

        summary.Scanned.Should().Be(0); // 非 Canvas 发件域不进入提取管线（v1 范围）
    }

    [Fact]
    public async Task Extract_DisabledBySetting_SkipsAndKeepsUnscanned()
    {
        await SeedCanvasMailAsync("digest-1", DigestHtmlBody);
        _settings.Data["schedule.enabled"] = "false";
        var service = NewService();

        (await service.ExtractPendingAsync(ct: CancellationToken.None)).Scanned.Should().Be(0);

        _settings.Data["schedule.enabled"] = "true"; // 重新打开后下一轮补扫（不丢数据）
        (await service.ExtractPendingAsync(ct: CancellationToken.None)).Inserted.Should().Be(2);
    }

    private async Task<ScheduleItem> SeedItemDueInHoursAsync(double hours)
    {
        var item = new ScheduleItem(
            Guid.NewGuid().ToString("N"), "acc-1", "src-1", "canvas",
            "Week 5 assignment", "IS6400 Business Data Analytics", "IS6400",
            "https://canvas.cityu.edu.hk/courses/71457/assignments/187001", "canvas-a:71457:187001",
            NowUtc.AddHours(hours), ScheduleItemStatus.Open, null, NowUtc, NowUtc);
        await _schedule.UpsertByDedupeKeyAsync(item, CancellationToken.None);
        return item;
    }

    [Fact]
    public async Task Remind_DueSoon_SendsExactlyOnce()
    {
        await SeedItemDueInHoursAsync(2);
        var service = NewService();

        (await service.CheckRemindersAsync(CancellationToken.None)).Should().Be(1);
        (await service.CheckRemindersAsync(CancellationToken.None)).Should().Be(0); // 同一 due 恰一次（AC7）
        _toasts.Sent.Should().HaveCount(1);
        _toasts.Sent[0].Title.Should().Be("作业截止提醒");
        _toasts.Sent[0].Body.Should().Contain("Week 5 assignment").And.Contain("还剩 2 小时");
    }

    [Fact]
    public async Task Remind_OutsideWindow_Silent()
    {
        await SeedItemDueInHoursAsync(30); // 默认窗口 24h
        (await NewService().CheckRemindersAsync(CancellationToken.None)).Should().Be(0);
        _toasts.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Remind_FinishedItem_Silent()
    {
        var item = await SeedItemDueInHoursAsync(2);
        await _schedule.SetStatusAsync(item.Id, ScheduleItemStatus.Done, CancellationToken.None);

        (await NewService().CheckRemindersAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Remind_DisabledBySettings_Silent()
    {
        await SeedItemDueInHoursAsync(2);
        var service = NewService();

        _settings.Data["schedule.reminder_enabled"] = "false";
        (await service.CheckRemindersAsync(CancellationToken.None)).Should().Be(0);

        _settings.Data.Remove("schedule.reminder_enabled");
        _settings.Data["schedule.enabled"] = "false"; // 总开关亦短路提醒
        (await service.CheckRemindersAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Remind_AfterDueChange_Rearms()
    {
        var item = await SeedItemDueInHoursAsync(2);
        var service = NewService();
        (await service.CheckRemindersAsync(CancellationToken.None)).Should().Be(1);

        var newDue = NowUtc.AddHours(3);
        var updated = item with { DueAtUtc = newDue };
        await _schedule.UpsertByDedupeKeyAsync(updated, CancellationToken.None); // due 变更清提醒标记

        (await service.CheckRemindersAsync(CancellationToken.None)).Should().Be(1); // 重新具备资格
        _toasts.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task Cleanup_KeepsRecentlyFinished_UntilRetention()
    {
        var fresh = await SeedItemDueInHoursAsync(24);
        await _schedule.SetStatusAsync(fresh.Id, ScheduleItemStatus.Done, CancellationToken.None);

        await NewService().ExtractPendingAsync(ct: CancellationToken.None); // 清理随提取运行

        var items = await _schedule.GetAllAsync("acc-1", CancellationToken.None);
        items.Should().ContainSingle(i => i.Id == fresh.Id); // 刚完成的保留（超 90 天清理走仓储级用例）
    }

    private sealed class FakeToastSender : IToastSender
    {
        public List<ToastNotification> Sent { get; } = [];

        public Task SendAsync(ToastNotification notification, CancellationToken ct)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeSettingsStore : ISettingsStore
    {
        public Dictionary<string, string> Data { get; } = [];

        public Task<string?> GetAsync(string key, CancellationToken ct) =>
            Task.FromResult(Data.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value, CancellationToken ct)
        {
            Data[key] = value;
            return Task.CompletedTask;
        }
    }
}
