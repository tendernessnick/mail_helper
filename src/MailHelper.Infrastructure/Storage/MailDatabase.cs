using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MailHelper.Infrastructure.Storage;

/// <summary>数据库就绪流程（04 章 §3.3）：
/// 1. PRAGMA integrity_check 失败 → 备份为 *.corrupt.bak → 重建（尽力恢复 rules/settings，失败则由内置规则 JSON 兜底）；
/// 2. EF 迁移（__EFMigrationsHistory 管理 schema 演进）；
/// 3. PRAGMA journal_mode=WAL（库级持久化）。</summary>
public static class MailDatabase
{
    public static void EnsureReady(string dbPath)
    {
        var fullPath = Path.GetFullPath(dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (File.Exists(fullPath) && !IntegrityOk(fullPath))
        {
            // EX-07 / STORE-001：损坏 → 备份 → 重建
            var backup = fullPath + ".corrupt.bak";
            File.Copy(fullPath, backup, overwrite: true);
            SqliteConnection.ClearAllPools(); // 释放句柄后才能删除
            File.Delete(fullPath);
            foreach (var sidecar in new[] { fullPath + "-wal", fullPath + "-shm" })
            {
                if (File.Exists(sidecar))
                {
                    File.Delete(sidecar);
                }
            }

            RunMigrations(fullPath);
            TryRecoverRulesAndSettings(fullPath, backup);
            SetWal(fullPath);
            return;
        }

        RunMigrations(fullPath);
        SetWal(fullPath);
    }

    /// <summary>仓储用短生命周期上下文：每次操作独立连接（每连接 PRAGMA）+ EF 上下文，连接随作用域释放。</summary>
    internal static async Task WithDbAsync(string dbPath, Func<MailHelperDbContext, Task> action, CancellationToken ct) =>
        await WithDbAsync<object?>(dbPath, async db =>
        {
            await action(db);
            return null;
        }, ct);

    internal static async Task<T> WithDbAsync<T>(string dbPath, Func<MailHelperDbContext, Task<T>> action, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(ConnectionString(dbPath));
        await connection.OpenAsync(ct);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";
            await command.ExecuteNonQueryAsync(ct);
        }

        var options = new DbContextOptionsBuilder<MailHelperDbContext>().UseSqlite(connection).Options;
        await using var db = new MailHelperDbContext(options);
        return await action(db);
    }

    internal static string ConnectionString(string dbPath) =>
        $"Data Source={Path.GetFullPath(dbPath)};Mode=ReadWriteCreate;Cache=Shared;Pooling=true";

    private static bool IntegrityOk(string dbPath)
    {
        try
        {
            using var connection = new SqliteConnection(ConnectionString(dbPath));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            return string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException)
        {
            return false; // 文件根本不是数据库
        }
    }

    private static void RunMigrations(string dbPath)
    {
        using var connection = new SqliteConnection(ConnectionString(dbPath));
        connection.Open();
        var options = new DbContextOptionsBuilder<MailHelperDbContext>().UseSqlite(connection).Options;
        using var db = new MailHelperDbContext(options);
        db.Database.Migrate();
    }

    private static void SetWal(string dbPath)
    {
        using var connection = new SqliteConnection(ConnectionString(dbPath));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
    }

    private static void TryRecoverRulesAndSettings(string dbPath, string backupPath)
    {
        try
        {
            using var connection = new SqliteConnection(ConnectionString(dbPath));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                ATTACH DATABASE @bak AS bak;
                INSERT OR REPLACE INTO rules SELECT * FROM bak.rules;
                INSERT OR REPLACE INTO settings SELECT * FROM bak.settings;
                DETACH DATABASE bak;
                """;
            command.Parameters.AddWithValue("@bak", backupPath);
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // 备份不可读则放弃恢复：预置规则可由随包 JSON 重新导入（04 §3.3 兜底路径）
        }
    }
}
