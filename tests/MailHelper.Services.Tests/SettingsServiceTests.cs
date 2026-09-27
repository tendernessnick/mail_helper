using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>设置服务（04 §7 配置项清单的类型化封装）：默认值、钳制、往返。</summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-settings-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(dbPath);
        _service = new SettingsService(new SettingsRepository(dbPath));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Defaults_MatchConfigTable()
    {
        (await _service.GetSyncIntervalMinutesAsync(CancellationToken.None)).Should().Be(5);
        (await _service.GetNotifyEnabledAsync("P0", CancellationToken.None)).Should().BeTrue();
        (await _service.GetNotifyEnabledAsync("P1", CancellationToken.None)).Should().BeTrue();
        (await _service.GetQuietHoursAsync(CancellationToken.None)).Should().BeNull();
        (await _service.GetLanguageAsync(CancellationToken.None)).Should().Be("auto");
        (await _service.GetBodyLimitMbAsync(CancellationToken.None)).Should().Be(2048);
        (await _service.IsDiagnosticsAsync(CancellationToken.None)).Should().BeFalse();
        (await _service.IsSyncOnStartupAsync(CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task SetSyncInterval_ClampedTo1Through60()
    {
        await _service.SetSyncIntervalMinutesAsync(0, CancellationToken.None);
        (await _service.GetSyncIntervalMinutesAsync(CancellationToken.None)).Should().Be(1);

        await _service.SetSyncIntervalMinutesAsync(999, CancellationToken.None);
        (await _service.GetSyncIntervalMinutesAsync(CancellationToken.None)).Should().Be(60);

        await _service.SetSyncIntervalMinutesAsync(15, CancellationToken.None);
        (await _service.GetSyncIntervalMinutesAsync(CancellationToken.None)).Should().Be(15);
    }

    [Fact]
    public async Task QuietHours_Roundtrip_AndClear()
    {
        await _service.SetQuietHoursAsync("23:00-07:00", CancellationToken.None);
        (await _service.GetQuietHoursAsync(CancellationToken.None)).Should().Be("23:00-07:00");

        await _service.SetQuietHoursAsync(null, CancellationToken.None);
        (await _service.GetQuietHoursAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task NotifyToggle_Roundtrip()
    {
        await _service.SetNotifyEnabledAsync("P0", false, CancellationToken.None);
        (await _service.GetNotifyEnabledAsync("P0", CancellationToken.None)).Should().BeFalse();
        (await _service.GetNotifyEnabledAsync("P1", CancellationToken.None)).Should().BeTrue(); // 独立开关
    }
}
