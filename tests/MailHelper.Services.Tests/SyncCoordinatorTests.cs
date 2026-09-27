using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Logging;
using MailHelper.Infrastructure.Storage;
using MailHelper.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>同步编排状态机（04 章 §2.2；EX-TC-04/05 断点续传语义；真实 SQLite 临时库）。</summary>
public class SyncCoordinatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-sync-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly AccountRepository _accounts;

    public SyncCoordinatorTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(_dbPath);
        _accounts = new AccountRepository(_dbPath);
        _accounts.UpsertAccountAsync(new Account(
            "acc-1", "s@connect.hku.hk", "测试", "t1", ChannelKind.Graph, null,
            AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private SyncCoordinator NewCoordinator(FakeMailProvider provider, int batchSize = 100, ILogger<SyncCoordinator>? logger = null) =>
        new(provider, new MailRepository(_dbPath), _accounts, "acc-1", logger, batchSize);

    private static RemoteMessage Msg(string id, ChangeKind kind = ChangeKind.Added, string preview = "p", string subject = "Hello") => new(
        id, $"<{id}@im>", subject, "Sender", "someone@hku.hk", preview, null,
        new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc), HasAttachments: false, IsRead: false, kind, "ck-" + id);

    private async Task SaveCheckpointAsync(string deltaLink) =>
        await _accounts.SaveCheckpointAsync(new SyncCheckpoint("acc-1", deltaLink, null, null, null), CancellationToken.None);

    [Fact]
    public async Task FullSync_UpsertsAllBatches_AndSavesCheckpoint()
    {
        var provider = new FakeMailProvider();
        provider.Pages.Add(new[] { Msg("m1"), Msg("m2") });
        provider.Pages.Add(new[] { Msg("m3"), Msg("m4") });
        provider.Pages.Add(new[] { Msg("m5"), Msg("m6") });
        provider.CompletedLink = "dl-new";
        var batches = new List<BatchSyncedEventArgs>();
        var coordinator = NewCoordinator(provider, batchSize: 2);
        coordinator.BatchSynced += (_, e) => batches.Add(e);

        await coordinator.SyncNowAsync(ct: CancellationToken.None);

        coordinator.State.Should().Be(SyncState.Idle);
        batches.Should().HaveCount(3); // 3 个入库批次
        batches.Sum(b => b.Added).Should().Be(6);
        batches.Select(b => b.PageIndex).Should().Equal(1, 2, 3);

        var pending = await new MailRepository(_dbPath).GetPendingClassificationAsync("acc-1", 100, CancellationToken.None);
        pending.Should().HaveCount(6);

        var checkpoint = await _accounts.GetCheckpointAsync("acc-1", CancellationToken.None);
        checkpoint!.DeltaLink.Should().Be("dl-new"); // 断点持久化（FR-04 AC2）
        checkpoint.LastSyncStatus.Should().Be("ok");
        checkpoint.LastSyncAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task IncrementalSync_UsesPersistedDeltaLink()
    {
        await SaveCheckpointAsync("dl-old");
        var provider = new FakeMailProvider { CompletedLink = "dl-new" };

        await NewCoordinator(provider).SyncNowAsync(ct: CancellationToken.None);

        provider.ReceivedDeltaLink.Should().Be("dl-old"); // 断点续传：用旧 deltaLink 增量拉取
    }

    [Fact]
    public async Task NoCheckpoint_AndFullIfNoLinkFalse_SkipsSync()
    {
        var provider = new FakeMailProvider { CompletedLink = "dl" };

        await NewCoordinator(provider).SyncNowAsync(fullIfNoLink: false, ct: CancellationToken.None);

        provider.FetchCalls.Should().Be(0);
        (await _accounts.GetCheckpointAsync("acc-1", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task InterruptedSync_KeepsOldDeltaLink_AndKeepsPartialData()
    {
        // EX-TC-05 / TC-009 语义：同步中途失败 → 已入库部分保留；重启后按旧 deltaLink 续传（upsert 幂等无重复）
        await SaveCheckpointAsync("dl-old");
        var provider = new FakeMailProvider
        {
            ThrowAfterPages = new MailProviderException("SYNC-001", "网络中断"),
        };
        provider.Pages.Add(new[] { Msg("m1"), Msg("m2") });
        var coordinator = NewCoordinator(provider);

        await coordinator.SyncNowAsync(ct: CancellationToken.None); // 异常被吞并映射状态

        coordinator.State.Should().Be(SyncState.Offline);
        var pending = await new MailRepository(_dbPath).GetPendingClassificationAsync("acc-1", 100, CancellationToken.None);
        pending.Should().HaveCount(2); // 部分数据已入库保留（EX-05）
        var checkpoint = await _accounts.GetCheckpointAsync("acc-1", CancellationToken.None);
        checkpoint!.DeltaLink.Should().Be("dl-old"); // 断点未推进
    }

    [Theory]
    [InlineData("SYNC-001", SyncState.Offline)]
    [InlineData("SYNC-002", SyncState.Error)]
    [InlineData("AUTH-003", SyncState.ReauthRequired)]
    public async Task ProviderErrors_MapToStates_AndRecordStatus(string errorCode, SyncState expected)
    {
        var provider = new FakeMailProvider
        {
            ThrowAfterPages = new MailProviderException(errorCode, "模拟错误"),
        };
        var states = new List<SyncStateChangedEventArgs>();
        var coordinator = NewCoordinator(provider);
        coordinator.StateChanged += (_, e) => states.Add(e);

        await coordinator.SyncNowAsync(ct: CancellationToken.None);

        coordinator.State.Should().Be(expected);
        states.Last().NewState.Should().Be(expected);
        states.Last().ErrorCode.Should().Be(errorCode);
        var checkpoint = await _accounts.GetCheckpointAsync("acc-1", CancellationToken.None);
        checkpoint!.LastSyncStatus.Should().Contain(errorCode); // last_sync_status 记录失败原因
    }

    [Fact]
    public async Task Preview_TruncatedTo500Chars_OnStore()
    {
        var provider = new FakeMailProvider();
        provider.Pages.Add(new[] { Msg("m1", preview: new string('预', 600)) });
        var coordinator = NewCoordinator(provider);

        await coordinator.SyncNowAsync(ct: CancellationToken.None);

        var pending = await new MailRepository(_dbPath).GetPendingClassificationAsync("acc-1", 10, CancellationToken.None);
        pending.Single().BodyPreview.Should().HaveLength(500); // 02 §9：预览仅存前 500 字符
    }

    [Fact]
    public async Task RunPeriodic_TicksRepeatedly_AndStopsOnCancel()
    {
        var provider = new FakeMailProvider { CompletedLink = "dl" };
        provider.Pages.Add(new[] { Msg("m1") });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var coordinator = NewCoordinator(provider);

        await coordinator.RunPeriodicAsync(TimeSpan.FromMilliseconds(60), cts.Token);

        provider.FetchCalls.Should().BeGreaterThanOrEqualTo(3); // 周期触发（FR-04）
        coordinator.State.Should().Be(SyncState.Idle);
    }

    [Fact]
    public async Task Serilog_WritesSyncCompletedToFile()
    {
        // 总控指令六.第 6 步：服务类模块用集成方式断言 Serilog 日志文件出现预期事件
        var logDir = Path.Combine(_dir, "logs");
        using var factory = MailHelperLogging.CreateFileLoggerFactory(logDir);
        var provider = new FakeMailProvider { CompletedLink = "dl" };
        provider.Pages.Add(new[] { Msg("m1"), Msg("m2") });
        var coordinator = NewCoordinator(provider, logger: factory.CreateLogger<SyncCoordinator>());

        await coordinator.SyncNowAsync(ct: CancellationToken.None);
        factory.Dispose(); // flush

        var logFile = Directory.GetFiles(logDir, "mailhelper-*.log").Single();
        var content = File.ReadAllText(logFile);
        content.Should().Contain("sync.completed");
        content.Should().Contain("added=2");
    }

    [Fact]
    public async Task RoundCompleted_FiresOnce_WithAllMails_AndInitialFlag()
    {
        // 04 §8.1「同步完成事件携带 newMails」（CHG-010）：整轮成功后触发一次，携带全部入库邮件
        var provider = new FakeMailProvider { CompletedLink = "dl-new" };
        provider.Pages.Add(new[] { Msg("m1"), Msg("m2") });
        provider.Pages.Add(new[] { Msg("m3") });
        var rounds = new List<SyncRoundCompletedEventArgs>();
        var coordinator = NewCoordinator(provider, batchSize: 2);
        coordinator.SyncRoundCompleted += (_, e) => rounds.Add(e);

        await coordinator.SyncNowAsync(ct: CancellationToken.None);

        rounds.Should().HaveCount(1); // 整轮一次（非每批）
        rounds[0].NewMails.Select(m => m.Id).Should().BeEquivalentTo("m1", "m2", "m3");
        rounds[0].IsInitialRound.Should().BeTrue(); // 同步前无断点 = 首轮（D-50 静默依据）
    }

    [Fact]
    public async Task RoundCompleted_IncrementalRound_FlagsFalse()
    {
        await SaveCheckpointAsync("dl-old");
        var provider = new FakeMailProvider { CompletedLink = "dl-new" };
        provider.Pages.Add(new[] { Msg("m1") });
        var rounds = new List<SyncRoundCompletedEventArgs>();
        var coordinator = NewCoordinator(provider);
        coordinator.SyncRoundCompleted += (_, e) => rounds.Add(e);

        await coordinator.SyncNowAsync(ct: CancellationToken.None);

        rounds.Single().IsInitialRound.Should().BeFalse();
    }

    [Fact]
    public async Task RoundCompleted_NotFired_OnFailure()
    {
        var provider = new FakeMailProvider
        {
            ThrowAfterPages = new MailProviderException("SYNC-001", "网络中断"),
        };
        provider.Pages.Add(new[] { Msg("m1") });
        var fired = 0;
        var coordinator = NewCoordinator(provider);
        coordinator.SyncRoundCompleted += (_, _) => fired++;

        await coordinator.SyncNowAsync(ct: CancellationToken.None);

        coordinator.State.Should().Be(SyncState.Offline);
        fired.Should().Be(0); // 失败轮不触发通知（避免半轮数据弹通知后又续传重复）
    }
}
