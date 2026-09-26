using FluentAssertions;
using MailHelper.Infrastructure.Storage;
using Xunit;

namespace MailHelper.Integration.Tests;

public class SettingsStoreTests : TempDirTestBase
{
    [Fact]
    public async Task Set_ThenGet_Roundtrips()
    {
        MailDatabase.EnsureReady(DbPath);
        var store = new SettingsRepository(DbPath);

        (await store.GetAsync("sync.interval_minutes", CancellationToken.None)).Should().BeNull();

        await store.SetAsync("sync.interval_minutes", "5", CancellationToken.None);
        (await store.GetAsync("sync.interval_minutes", CancellationToken.None)).Should().Be("5");

        await store.SetAsync("sync.interval_minutes", "10", CancellationToken.None); // 覆盖更新
        (await store.GetAsync("sync.interval_minutes", CancellationToken.None)).Should().Be("10");
    }
}
