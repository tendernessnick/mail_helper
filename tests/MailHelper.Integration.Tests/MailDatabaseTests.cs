using FluentAssertions;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>数据库就绪流程（04 章 §3.3：integrity_check 重建 EX-07 / 迁移 / WAL）。</summary>
public class MailDatabaseTests : TempDirTestBase
{
    [Fact]
    public async Task EnsureReady_OnGarbageFile_BackupsAndRebuilds()
    {
        File.WriteAllText(DbPath, "这根本不是一个 SQLite 数据库文件 garbage bytes 12345");

        var act = () => MailDatabase.EnsureReady(DbPath);
        act.Should().NotThrow(); // EX-07：检测损坏 → 自动重建，不崩溃

        File.Exists(DbPath + ".corrupt.bak").Should().BeTrue(); // 旧库备份保留

        // 重建后立即可用
        var accounts = new AccountRepository(DbPath);
        await accounts.UpsertAccountAsync(NewAccount(), CancellationToken.None);
        var repo = new MailRepository(DbPath);
        await repo.UpsertRangeAsync("acc-1", new[] { NewMessage("m1") }, CancellationToken.None);
        (await repo.GetPendingClassificationAsync("acc-1", 10, CancellationToken.None))
            .Should().ContainSingle(m => m.Id == "m1");
    }

    [Fact]
    public void EnsureReady_SetsWalMode()
    {
        MailDatabase.EnsureReady(DbPath);

        using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadWrite");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        cmd.ExecuteScalar().Should().Be("wal"); // 04 §3.3 连接与 PRAGMA 约定
    }

    [Fact]
    public void EnsureReady_IsIdempotent_OnHealthyDb()
    {
        MailDatabase.EnsureReady(DbPath);
        var act = () => MailDatabase.EnsureReady(DbPath);
        act.Should().NotThrow();
    }
}
