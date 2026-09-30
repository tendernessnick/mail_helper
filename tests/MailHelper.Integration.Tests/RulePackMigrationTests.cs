using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services.Rules;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>S13-B 内置规则包版本迁移：版本变化→rule 来源邮件置回待处理（分类管线重跑）
/// + 同步断点清空（全量重拉，回填修复期缺失的发件地址）；用户改判（user 来源）永不触碰。</summary>
public class RulePackMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-rulemig-" + Guid.NewGuid().ToString("N"));
    private readonly MailRepository _mails;
    private readonly AccountRepository _accounts;
    private readonly SettingsRepository _settings;
    private readonly RulePackMigrator _migrator;

    public RulePackMigrationTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(dbPath);
        new AccountRepository(dbPath).UpsertAccountAsync(new Account(
            "acc-1", "s@my.cityu.edu.hk", "测试", "t1", ChannelKind.OutlookDesktop, null,
            AccountStatus.Active, new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None)
            .GetAwaiter().GetResult();
        _mails = new MailRepository(dbPath);
        _accounts = new AccountRepository(dbPath);
        _settings = new SettingsRepository(dbPath);
        _migrator = new RulePackMigrator(_mails, _accounts, _settings);
        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        // 贴近真实存储值：EngineName='rule-engine'（非 'rule'）；llm 来源同为机器判定应回炉
        MailMessage Msg(string id, string classifiedBy, double confidence) => new(
            id, "acc-1", $"<{id}@im>", id, "f", "f@cityu.edu.hk", "p", null,
            new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc), false, false,
            MailCategory.Other, Importance.P2, confidence, classifiedBy,
            new DateTime(2026, 9, 28, 8, 30, 0, DateTimeKind.Utc), null, false);
        await _mails.UpsertRangeAsync("acc-1",
            [Msg("r1", "rule-engine", 0.4), Msg("r2", "rule-engine", 0.9), Msg("l1", "llm", 0.8),
             Msg("u1", "user", 0.9)], CancellationToken.None);
        await _accounts.SaveCheckpointAsync(new SyncCheckpoint(
            "acc-1", DeltaLink: "outlook://inbox?received=20260928T080000Z", ImapUidWatermark: null,
            LastSyncAtUtc: new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc), LastSyncStatus: "Idle"),
            CancellationToken.None);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task VersionChanged_ResetsRuleMails_KeepsUserVerdicts_ClearsBreakpoint()
    {
        var reset = await _migrator.MigrateIfNeededAsync("2026.10", "acc-1", CancellationToken.None);

        reset.Should().Be(3); // r1/r2（rule-engine）+ l1（llm），u1（user）保留
        (await _mails.GetByIdAsync("r1", CancellationToken.None))!.ClassifiedAtUtc.Should().BeNull();
        (await _mails.GetByIdAsync("r2", CancellationToken.None))!.ClassifiedAtUtc.Should().BeNull();
        var user = await _mails.GetByIdAsync("u1", CancellationToken.None); // 用户改判保留（EX-08 精神）
        user!.ClassifiedAtUtc.Should().NotBeNull();
        user.ClassifiedBy.Should().Be("user");
        var cp = await _accounts.GetCheckpointAsync("acc-1", CancellationToken.None); // 断点清空→下轮全量重拉
        cp!.DeltaLink.Should().BeNull();
        (await _settings.GetAsync(RulePackMigrator.AppliedVersionKey, CancellationToken.None)).Should().Be("2026.10");
    }

    [Fact]
    public async Task SameVersion_NoOp()
    {
        await _migrator.MigrateIfNeededAsync("2026.10", "acc-1", CancellationToken.None); // 首次迁移：r1/r2 置回待处理
        // 模拟分类管线按新规则重跑完成（写回 r1 分类）
        await _mails.ApplyClassificationRangeAsync(
            [new ClassificationWrite("r1", MailCategory.Course, Importance.P1, 0.9, "rule")], CancellationToken.None);

        var reset = await _migrator.MigrateIfNeededAsync("2026.10", "acc-1", CancellationToken.None);

        reset.Should().Be(0); // 版本未变：不重复回炉
        (await _mails.GetByIdAsync("r1", CancellationToken.None))!.ClassifiedAtUtc.Should().NotBeNull();
        // 首次迁移已清空断点，二次 no-op 不再改动（保持 null，等待下轮全量同步写回）
        (await _accounts.GetCheckpointAsync("acc-1", CancellationToken.None))!.DeltaLink.Should().BeNull();
    }

    [Fact]
    public async Task NoCheckpoint_StillMigrates()
    {
        await _accounts.SaveCheckpointAsync(new SyncCheckpoint(
            "acc-1", null, null, null, null), CancellationToken.None);

        var act = () => _migrator.MigrateIfNeededAsync("2026.10", "acc-1", CancellationToken.None);

        var reset = await act.Should().NotThrowAsync();
        reset.Which.Should().Be(3);
    }

    [Fact]
    public async Task FirstRun_AppliedVersionNull_TriggersMigration()
    {
        // 存量库升级场景：applied_version 键不存在（null）→ 视为版本变化，修复 from_address 缺失存量
        (await _settings.GetAsync(RulePackMigrator.AppliedVersionKey, CancellationToken.None)).Should().BeNull();

        var reset = await _migrator.MigrateIfNeededAsync("2026.09", "acc-1", CancellationToken.None);

        reset.Should().Be(3);
    }
}
