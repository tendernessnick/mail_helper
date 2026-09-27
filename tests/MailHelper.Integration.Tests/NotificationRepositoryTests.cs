using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Infrastructure.Storage;
using MailHelper.Core;
using MailHelper.Core.Domain;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>notification_log 仓储（04 §8：message_id 去重 + 勿扰 queued→flushed 流转；真 SQLite）。</summary>
public class NotificationRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-notifyrepo-" + Guid.NewGuid().ToString("N"));
    private readonly NotificationRepository _repo;

    public NotificationRepositoryTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(dbPath);
        // FK 链：messages.account_id → accounts、notification_log.message_id → messages
        new AccountRepository(dbPath).UpsertAccountAsync(new Account(
            "acc-1", "s@connect.hku.hk", "测试", "t1", ChannelKind.Graph, null,
            AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None)
            .GetAwaiter().GetResult();
        // 先种邮件行（生产顺序：先入库再通知）
        var ids = new[] { "a", "b", "c", "q1", "q2", "s1", "m1" }.Select(id => new MailMessage(
            id, "acc-1", $"<{id}@im>", id, "f", "f@hku.hk", "p", null,
            new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), false, false,
            MailCategory.Other, Importance.P2, 0.5, "rule", null, null, false)).ToList();
        new MailRepository(dbPath).UpsertRangeAsync("acc-1", ids, CancellationToken.None).GetAwaiter().GetResult();
        _repo = new NotificationRepository(dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static NotificationLogEntry Entry(string id, string level = "P0") =>
        new(id, level, new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task FilterUnnotified_ReturnsOnlyMissing()
    {
        await _repo.LogRangeAsync([Entry("a"), Entry("b")], CancellationToken.None);

        var missing = await _repo.FilterUnnotifiedAsync(["a", "b", "c"], CancellationToken.None);

        missing.Should().BeEquivalentTo(["c"]); // NOT EXISTS(notification_log)（04 §8.4）
    }

    [Fact]
    public async Task QueuedLifecycle_CountAndFlush()
    {
        await _repo.LogRangeAsync([Entry("q1", "queued"), Entry("q2", "queued"), Entry("s1", "P0")], CancellationToken.None);
        (await _repo.CountQueuedAsync(CancellationToken.None)).Should().Be(2);

        var flushed = await _repo.FlushQueuedAsync(CancellationToken.None);

        flushed.Should().Be(2); // 勿扰结束补发摘要的基数
        (await _repo.CountQueuedAsync(CancellationToken.None)).Should().Be(0);
        // flushed 行保留参与去重：flushed 后仍不应重发
        (await _repo.FilterUnnotifiedAsync(["q1", "q2", "s1"], CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task LogRange_IsIdempotent_OnReplay()
    {
        var entries = new[] { Entry("m1"), Entry("m1") }; // 重放（EX-05 续传重灌同批）

        await _repo.LogRangeAsync(entries, CancellationToken.None);
        await _repo.LogRangeAsync(entries, CancellationToken.None);

        (await _repo.FilterUnnotifiedAsync(["m1"], CancellationToken.None)).Should().BeEmpty();
    }
}
