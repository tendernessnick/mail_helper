using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Storage;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>账户与同步断点仓储集成测试（FR-04 AC2：deltaLink 断点持久化）。</summary>
public class AccountStoreTests : TempDirTestBase
{
    [Fact]
    public async Task UpsertAccount_ThenFind_Roundtrips()
    {
        MailDatabase.EnsureReady(DbPath);
        var store = new AccountRepository(DbPath);

        await store.UpsertAccountAsync(NewAccount(), CancellationToken.None);

        var found = await store.FindAccountAsync("acc-1", CancellationToken.None);
        found.Should().NotBeNull();
        found!.Email.Should().Be("acc-1@connect.hku.hk");
        found.DisplayName.Should().Be("测试账户");
        found.TenantId.Should().Be("tenant-1");
        found.Channel.Should().Be(ChannelKind.Graph);
        found.Status.Should().Be(AccountStatus.Active);
        found.CreatedAtUtc.Should().Be(new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc));

        (await store.FindAccountAsync("missing", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task UpsertAccount_UpdatesExistingRow()
    {
        MailDatabase.EnsureReady(DbPath);
        var store = new AccountRepository(DbPath);

        await store.UpsertAccountAsync(NewAccount(), CancellationToken.None);
        var updated = NewAccount() with { Status = AccountStatus.ReauthRequired, DisplayName = "改密后" };
        await store.UpsertAccountAsync(updated, CancellationToken.None); // AUTH-003 场景

        var found = await store.FindAccountAsync("acc-1", CancellationToken.None);
        found!.Status.Should().Be(AccountStatus.ReauthRequired);
        found.DisplayName.Should().Be("改密后");
    }

    [Fact]
    public async Task Checkpoint_SaveThenLoad_Roundtrips()
    {
        MailDatabase.EnsureReady(DbPath);
        var store = new AccountRepository(DbPath);
        await store.UpsertAccountAsync(NewAccount(), CancellationToken.None);

        (await store.GetCheckpointAsync("acc-1", CancellationToken.None)).Should().BeNull(); // 首次同步前

        var checkpoint = new SyncCheckpoint(
            "acc-1",
            DeltaLink: "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=abc123",
            ImapUidWatermark: null,
            LastSyncAtUtc: new DateTime(2026, 9, 26, 9, 15, 0, DateTimeKind.Utc),
            LastSyncStatus: "ok");
        await store.SaveCheckpointAsync(checkpoint, CancellationToken.None);

        var loaded = await store.GetCheckpointAsync("acc-1", CancellationToken.None);
        loaded.Should().BeEquivalentTo(checkpoint); // 断点续传：重启后可取回

        var again = checkpoint with { DeltaLink = "https://graph.microsoft.com/...?$deltatoken=next" };
        await store.SaveCheckpointAsync(again, CancellationToken.None);
        (await store.GetCheckpointAsync("acc-1", CancellationToken.None)).Should().BeEquivalentTo(again);
    }
}
