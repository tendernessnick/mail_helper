using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Storage;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>收件箱查询侧 API（FR-12：过滤/排序/未读数/待确认计数/已读标记）。</summary>
public class InboxQueryTests : TempDirTestBase
{
    private async Task<(AccountRepository Accounts, MailRepository Repo)> PrepareAsync(params MailMessage[] messages)
    {
        MailDatabase.EnsureReady(DbPath);
        var accounts = new AccountRepository(DbPath);
        await accounts.UpsertAccountAsync(NewAccount(), CancellationToken.None);
        var repo = new MailRepository(DbPath);
        if (messages.Length > 0)
        {
            await repo.UpsertRangeAsync("acc-1", messages, CancellationToken.None);
        }

        return (accounts, repo);
    }

    private static MailMessage Msg(string id, string category, Importance importance,
        DateTime received, bool isRead = false, bool deleted = false, double? confidence = null, DateTime? classifiedAt = null) => new(
        id, "acc-1", $"<{id}@im>", $"Subject {id}", "Sender", "someone@hku.hk", $"preview {id}", null,
        received, false, isRead, category, importance, confidence, "rule-engine", classifiedAt, "ck", deleted);

    [Fact]
    public async Task GetInbox_OrdersByImportanceThenReceived_ExcludesDeleted()
    {
        var (_, repo) = await PrepareAsync(
            Msg("a", CategoryIds.Course, Importance.P1, new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc)),
            Msg("b", CategoryIds.Finance, Importance.P0, new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc)),
            Msg("c", CategoryIds.Course, Importance.P1, new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc)),
            Msg("d", CategoryIds.Course, Importance.P2, new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc)),
            Msg("x", CategoryIds.Course, Importance.P0, new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc), deleted: true));

        var inbox = await repo.GetInboxAsync("acc-1", new InboxQuery(), CancellationToken.None);

        inbox.Select(m => m.Id).Should().Equal("b", "c", "a", "d"); // 重要度降序 → 时间降序；远端已删不显示
    }

    [Fact]
    public async Task GetInbox_FiltersByCategory_AndUnread_AndMinImportance()
    {
        var (_, repo) = await PrepareAsync(
            Msg("c1", CategoryIds.Course, Importance.P2, new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc)),
            Msg("c2", CategoryIds.Course, Importance.P2, new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc), isRead: true),
            Msg("f1", CategoryIds.Finance, Importance.P1, new DateTime(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc)));

        (await repo.GetInboxAsync("acc-1", new InboxQuery(Category: CategoryIds.Course), CancellationToken.None))
            .Select(m => m.Id).Should().Equal("c1", "c2");
        (await repo.GetInboxAsync("acc-1", new InboxQuery(UnreadOnly: true), CancellationToken.None))
            .Select(m => m.Id).Should().Equal("f1", "c1"); // P1 的 f1 排在 P2 的 c1 前（重要度优先）
        (await repo.GetInboxAsync("acc-1", new InboxQuery(MinimumImportance: Importance.P1), CancellationToken.None))
            .Select(m => m.Id).Should().Equal("f1");
    }

    [Fact]
    public async Task GetInbox_NeedsReviewOnly_ReturnsClassifiedLowConfidence()
    {
        var (_, repo) = await PrepareAsync(
            Msg("low", CategoryIds.Other, Importance.P2, new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc),
                confidence: 0.0, classifiedAt: new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc)),
            Msg("high", CategoryIds.Course, Importance.P2, new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc),
                confidence: 0.9, classifiedAt: new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc)),
            Msg("pending", CategoryIds.Other, Importance.P2, new DateTime(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc),
                confidence: 0.0, classifiedAt: null)); // 未分类不算待确认

        var review = await repo.GetInboxAsync("acc-1", new InboxQuery(NeedsReviewOnly: true), CancellationToken.None);

        review.Select(m => m.Id).Should().Equal("low"); // FR-09：仅「已分类且低置信度」
    }

    [Fact]
    public async Task UnreadCounts_PerCategory_Aggregated()
    {
        var (_, repo) = await PrepareAsync(
            Msg("c1", CategoryIds.Course, Importance.P2, new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc)),
            Msg("c2", CategoryIds.Course, Importance.P2, new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc), isRead: true),
            Msg("f1", CategoryIds.Finance, Importance.P1, new DateTime(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc)));

        var counts = await repo.GetUnreadCountsAsync("acc-1", CancellationToken.None);

        counts.Should().HaveCount(7);
        counts[CategoryIds.Course].Should().Be(1);
        counts[CategoryIds.Finance].Should().Be(1);
        counts[CategoryIds.Subscription].Should().Be(0);
    }

    [Fact]
    public async Task NeedsReviewCount_UsesThreshold()
    {
        var (_, repo) = await PrepareAsync(
            Msg("low", CategoryIds.Other, Importance.P2, new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc),
                confidence: 0.3, classifiedAt: new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc)),
            Msg("mid", CategoryIds.Other, Importance.P2, new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc),
                confidence: 0.6, classifiedAt: new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc)));

        (await repo.GetNeedsReviewCountAsync("acc-1", 0.55, CancellationToken.None)).Should().Be(1);
        (await repo.GetNeedsReviewCountAsync("acc-1", 0.7, CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task MarkRead_TogglesLocalOnly()
    {
        var (_, repo) = await PrepareAsync(
            Msg("m1", CategoryIds.Course, Importance.P2, new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc)));

        await repo.MarkReadAsync("m1", CancellationToken.None);

        var inbox = await repo.GetInboxAsync("acc-1", new InboxQuery(UnreadOnly: true), CancellationToken.None);
        inbox.Should().BeEmpty(); // 已读（仅本地，不回写远端）
    }
}
