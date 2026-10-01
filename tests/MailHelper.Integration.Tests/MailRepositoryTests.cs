using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Storage;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>MailRepository 集成测试（临时 SQLite 库；06 章 TC-008/TC-010/TC-017 对应能力）。</summary>
public class MailRepositoryTests : TempDirTestBase
{
    private async Task<MailRepository> PrepareAsync(params MailMessage[] messages)
    {
        MailDatabase.EnsureReady(DbPath);
        var accounts = new AccountRepository(DbPath);
        await accounts.UpsertAccountAsync(NewAccount(), CancellationToken.None);
        var repo = new MailRepository(DbPath);
        if (messages.Length > 0)
        {
            await repo.UpsertRangeAsync("acc-1", messages, CancellationToken.None);
        }

        return repo;
    }

    [Fact]
    public async Task UpsertRange_InsertsAndRoundtrips()
    {
        var repo = await PrepareAsync(
            NewMessage("m1"),
            NewMessage("m2", subject: "学费缴纳提醒", preview: "请于 9 月 30 日前缴清学费"),
            NewMessage("m3", received: new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc)));

        var pending = await repo.GetPendingClassificationAsync("acc-1", 10, CancellationToken.None);
        pending.Should().HaveCount(3);

        var m2 = pending.Single(m => m.Id == "m2");
        m2.Subject.Should().Be("学费缴纳提醒");
        m2.BodyPreview.Should().StartWith("请于 9 月 30 日");
        m2.ReceivedAtUtc.Should().Be(new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc)); // Unix 秒往返无损
        m2.Category.Should().Be(CategoryIds.Other); // 新行默认待分类（CHG-002：显式 P2/other）
        m2.Importance.Should().Be(Importance.P2);
        m2.ClassifiedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task UpsertRange_IsIdempotentByPrimaryKey()
    {
        var batch = new[] { NewMessage("m1"), NewMessage("m2"), NewMessage("m3") };
        var repo = await PrepareAsync(batch);

        var insertedAgain = await repo.UpsertRangeAsync("acc-1", batch, CancellationToken.None); // deltaLink 重放
        insertedAgain.Should().Be(0);

        var pending = await repo.GetPendingClassificationAsync("acc-1", 10, CancellationToken.None);
        pending.Should().HaveCount(3); // 无重复记录（FR-04 AC2 / TC-008）
    }

    [Fact]
    public async Task UpsertRange_UpdatesSyncFields_KeepsClassification()
    {
        var repo = await PrepareAsync(NewMessage("m1", subject: "Tuition original"));

        // 用户改判（EX-08：人工分类不被同步覆盖）
        await repo.ApplyClassificationRangeAsync(new[]
        {
            new ClassificationWrite("m1", CategoryIds.Finance, Importance.P1, 0.9, "user"),
        }, CancellationToken.None);

        // 远端同一封邮件更新（已读、主题变化）再次同步入库
        await repo.UpsertRangeAsync("acc-1", new[]
        {
            NewMessage("m1", subject: "Tuition revised final", isRead: true),
        }, CancellationToken.None);

        var found = await repo.SearchFtsAsync("acc-1", "revised", 10, CancellationToken.None);
        found.Should().ContainSingle(m => m.Id == "m1");
        var m = found.Single(m => m.Id == "m1");
        m.Subject.Should().Be("Tuition revised final"); // 同步字段已更新
        m.IsRead.Should().BeTrue();
        m.Category.Should().Be(CategoryIds.Finance);   // 分类保留
        m.Importance.Should().Be(Importance.P1);
        m.Confidence.Should().Be(0.9);
        m.ClassifiedBy.Should().Be("user");
    }

    [Fact]
    public async Task GetPendingClassification_ReturnsOnlyUnclassifiedInReceivedOrder()
    {
        var repo = await PrepareAsync(
            NewMessage("m1", received: new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc)),
            NewMessage("m2", received: new DateTime(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc)),
            NewMessage("m3", received: new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc)),
            NewMessage("m4", received: new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc)),
            NewMessage("m5", received: new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc)));

        await repo.ApplyClassificationRangeAsync(new[]
        {
            new ClassificationWrite("m1", CategoryIds.Course, Importance.P2, 1.0, "rule-engine"),
            new ClassificationWrite("m3", CategoryIds.Course, Importance.P2, 1.0, "rule-engine"),
        }, CancellationToken.None);

        var pending = await repo.GetPendingClassificationAsync("acc-1", 10, CancellationToken.None);
        pending.Select(m => m.Id).Should().Equal("m5", "m4", "m2"); // 接收时间升序

        var batch = await repo.GetPendingClassificationAsync("acc-1", 2, CancellationToken.None);
        batch.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyClassificationRange_WritesBackAllFields()
    {
        var repo = await PrepareAsync(NewMessage("m1", subject: "Moodle coursework"));

        var before = DateTime.UtcNow.AddSeconds(-5);
        await repo.ApplyClassificationRangeAsync(new[]
        {
            new ClassificationWrite("m1", CategoryIds.Course, Importance.P1, 0.875, "rule-engine"),
        }, CancellationToken.None);

        var pending = await repo.GetPendingClassificationAsync("acc-1", 10, CancellationToken.None);
        pending.Should().BeEmpty(); // 已分类不再待处理

        var found = await repo.SearchFtsAsync("acc-1", "coursework", 10, CancellationToken.None);
        var m = found.Single(m => m.Id == "m1");
        m.Category.Should().Be(CategoryIds.Course);
        m.Importance.Should().Be(Importance.P1);
        m.Confidence.Should().Be(0.875);
        m.ClassifiedBy.Should().Be("rule-engine");
        m.ClassifiedAtUtc.Should().NotBeNull().And.BeOnOrAfter(before);
    }

    [Fact]
    public async Task SearchFts_MatchesChineseAndEnglish()
    {
        var repo = await PrepareAsync(
            NewMessage("eng", subject: "Tuition Fee Payment Reminder", preview: "please settle by 30 Sep"),
            NewMessage("zh2", subject: "周会通知", preview: "本周请关注缴纳学费的截止时间"),
            NewMessage("from", subject: "缴费通知", fromName: "财务处", preview: "see attachment"));

        (await repo.SearchFtsAsync("acc-1", "tuition", 10, CancellationToken.None))
            .Should().ContainSingle(m => m.Id == "eng");                       // 英文词（trigram MATCH）
        (await repo.SearchFtsAsync("acc-1", "学费", 10, CancellationToken.None))
            .Should().ContainSingle(m => m.Id == "zh2");                       // 2 字中文（LIKE 兜底，D-22）
        (await repo.SearchFtsAsync("acc-1", "财务处", 10, CancellationToken.None))
            .Should().ContainSingle(m => m.Id == "from");                      // 3 字中文命中 from_name
        (await repo.SearchFtsAsync("acc-1", "invit", 10, CancellationToken.None))
            .Should().BeEmpty();                                              // 不存在的关键词
    }

    [Fact]
    public async Task SearchFts_IndexFollowsUpdates()
    {
        var repo = await PrepareAsync(NewMessage("m1", subject: "Alpha Beta"));

        (await repo.SearchFtsAsync("acc-1", "gamma", 10, CancellationToken.None)).Should().BeEmpty();

        await repo.UpsertRangeAsync("acc-1", new[] { NewMessage("m1", subject: "Gamma Delta") }, CancellationToken.None);

        (await repo.SearchFtsAsync("acc-1", "gamma", 10, CancellationToken.None))
            .Should().ContainSingle(m => m.Id == "m1"); // messages_au 触发器同步 FTS
    }

    [Fact]
    public async Task SearchFts_RespectsAccountId()
    {
        MailDatabase.EnsureReady(DbPath);
        var accounts = new AccountRepository(DbPath);
        await accounts.UpsertAccountAsync(NewAccount("acc-1"), CancellationToken.None);
        await accounts.UpsertAccountAsync(NewAccount("acc-2"), CancellationToken.None);
        var repo = new MailRepository(DbPath);
        await repo.UpsertRangeAsync("acc-1", new[] { NewMessage("a1", subject: "shared keyword alpha") }, CancellationToken.None);
        await repo.UpsertRangeAsync("acc-2", new[] { NewMessage("b1", accountId: "acc-2", subject: "shared keyword beta") }, CancellationToken.None);

        var results = await repo.SearchFtsAsync("acc-1", "shared", 10, CancellationToken.None);
        results.Should().ContainSingle(m => m.Id == "a1");
    }
}
