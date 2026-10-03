using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>schedule_items 仓储（S18/CHG-015；(account_id, dedupe_key) 唯一；upsert 更新语义；
/// open 升序+终态垫底排序；终态保留期清理。真 SQLite + 原生 SQL 做时间前移）。</summary>
public class ScheduleRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-schedrepo-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ScheduleRepository _repo;

    public ScheduleRepositoryTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(_dbPath); // 迁移须建出 schedule_items（含 AddScheduleModule）
        _repo = new ScheduleRepository(_dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static ScheduleItem Item(string key, DateTime dueUtc, ScheduleItemStatus status = ScheduleItemStatus.Open,
        long? remindedDueAt = null, string? title = "Week 5 assignment") => new(
        Guid.NewGuid().ToString("N"), "acc-1", "msg-1", "canvas",
        title!, "IS6400 Business Data Analytics", "IS6400",
        "https://canvas.cityu.edu.hk/courses/71457/assignments/187001", key,
        dueUtc, status, remindedDueAt, DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public async Task Upsert_NewKey_Inserts()
    {
        var (item, inserted, dueUpdated) = await _repo.UpsertByDedupeKeyAsync(
            Item("canvas-a:1:1", new DateTime(2026, 10, 9, 15, 59, 0, DateTimeKind.Utc)), CancellationToken.None);

        inserted.Should().BeTrue();
        dueUpdated.Should().BeFalse();
        item.Id.Should().NotBeEmpty();
        (await _repo.CountOpenAsync("acc-1", CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task Upsert_SameKey_DueChanged_UpdatesAndClearsReminder()
    {
        var due = new DateTime(2026, 10, 9, 15, 59, 0, DateTimeKind.Utc);
        var first = await _repo.UpsertByDedupeKeyAsync(
            Item("canvas-a:1:1", due, remindedDueAt: ((DateTimeOffset)due).ToUnixTimeSeconds()),
            CancellationToken.None);
        first.Inserted.Should().BeTrue();

        var (updated, inserted, dueUpdated) = await _repo.UpsertByDedupeKeyAsync(
            Item("canvas-a:1:1", due.AddHours(6)), CancellationToken.None);

        inserted.Should().BeFalse();
        dueUpdated.Should().BeTrue();
        updated.DueAtUtc.Should().Be(due.AddHours(6));
        updated.RemindedDueAt.Should().BeNull(); // 变更后恢复提醒资格（US-18-2）
        updated.Id.Should().Be(first.Item.Id); // 同键同条目
    }

    [Fact]
    public async Task Upsert_SameKey_SameDue_KeepsReminderMarker()
    {
        var due = new DateTime(2026, 10, 9, 15, 59, 0, DateTimeKind.Utc);
        var reminded = ((DateTimeOffset)due).ToUnixTimeSeconds();
        await _repo.UpsertByDedupeKeyAsync(Item("canvas-a:1:1", due, remindedDueAt: reminded),
            CancellationToken.None);

        var (_, inserted, dueUpdated) = await _repo.UpsertByDedupeKeyAsync(
            Item("canvas-a:1:1", due), CancellationToken.None); // 周报重放同 due

        inserted.Should().BeFalse();
        dueUpdated.Should().BeFalse();
        var all = await _repo.GetAllAsync("acc-1", CancellationToken.None);
        all.Should().ContainSingle().Which.RemindedDueAt.Should().Be(reminded); // 已提醒资格不被重放清除（AC7）
    }

    [Fact]
    public async Task GetAll_OpenAscending_FinishedLast()
    {
        var baseUtc = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
        await _repo.UpsertByDedupeKeyAsync(Item("k-late", baseUtc.AddDays(5)), CancellationToken.None);
        await _repo.UpsertByDedupeKeyAsync(Item("k-soon", baseUtc.AddDays(1)), CancellationToken.None);
        await _repo.UpsertByDedupeKeyAsync(Item("k-done", baseUtc.AddDays(-1), ScheduleItemStatus.Done),
            CancellationToken.None);

        var all = await _repo.GetAllAsync("acc-1", CancellationToken.None);

        all.Select(i => i.DedupeKey).Should().ContainInConsecutiveOrder("k-soon", "k-late", "k-done");
    }

    [Fact]
    public async Task SetStatus_And_CountOpen()
    {
        var a = await _repo.UpsertByDedupeKeyAsync(Item("k-1", DateTime.UtcNow), CancellationToken.None);
        await _repo.UpsertByDedupeKeyAsync(Item("k-2", DateTime.UtcNow), CancellationToken.None);

        await _repo.SetStatusAsync(a.Item.Id, ScheduleItemStatus.Ignored, CancellationToken.None);

        (await _repo.CountOpenAsync("acc-1", CancellationToken.None)).Should().Be(1);
        var all = await _repo.GetAllAsync("acc-1", CancellationToken.None);
        all.Single(i => i.Id == a.Item.Id).Status.Should().Be(ScheduleItemStatus.Ignored);
    }

    [Fact]
    public async Task MarkReminded_Persists()
    {
        var due = new DateTime(2026, 10, 9, 15, 59, 0, DateTimeKind.Utc);
        var (item, _, _) = await _repo.UpsertByDedupeKeyAsync(Item("k-r", due), CancellationToken.None);
        var unix = ((DateTimeOffset)due).ToUnixTimeSeconds();

        await _repo.MarkRemindedAsync(item.Id, unix, CancellationToken.None);

        var all = await _repo.GetAllAsync("acc-1", CancellationToken.None);
        all.Should().ContainSingle().Which.RemindedDueAt.Should().Be(unix);
    }

    [Fact]
    public async Task DeleteFinishedBefore_RemovesOnlyOldFinished()
    {
        var baseUtc = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
        var oldDone = (await _repo.UpsertByDedupeKeyAsync(Item("k-old", baseUtc), CancellationToken.None)).Item;
        var freshDone = (await _repo.UpsertByDedupeKeyAsync(Item("k-fresh", baseUtc), CancellationToken.None)).Item;
        var open = (await _repo.UpsertByDedupeKeyAsync(Item("k-open", baseUtc), CancellationToken.None)).Item;
        await _repo.SetStatusAsync(oldDone.Id, ScheduleItemStatus.Done, CancellationToken.None);
        await _repo.SetStatusAsync(freshDone.Id, ScheduleItemStatus.Done, CancellationToken.None);
        await _repo.SetStatusAsync(open.Id, ScheduleItemStatus.Ignored, CancellationToken.None);

        // 原生 SQL 仅把 oldDone 的 updated_at_utc 前移 120 天（仓储无改时接口，测试直写列）
        await using var connection = new SqliteConnection($"Data Source={Path.GetFullPath(_dbPath)}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE schedule_items SET updated_at_utc = $old WHERE id = $id";
        command.Parameters.AddWithValue("$old",
            new DateTimeOffset(baseUtc.AddDays(-120), TimeSpan.Zero).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$id", oldDone.Id);
        await command.ExecuteNonQueryAsync();
        await _repo.SetStatusAsync(open.Id, ScheduleItemStatus.Open, CancellationToken.None); // open 恢复，永不清理

        var deleted = await _repo.DeleteFinishedBeforeAsync(baseUtc.AddDays(-90), CancellationToken.None);

        deleted.Should().Be(1);
        var all = await _repo.GetAllAsync("acc-1", CancellationToken.None);
        all.Select(i => i.DedupeKey).Should().BeEquivalentTo(["k-fresh", "k-open"]);
    }
}
