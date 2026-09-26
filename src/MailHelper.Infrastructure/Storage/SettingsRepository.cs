using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.Storage;

/// <summary>settings 键值仓储（ISettingsStore 实现；04 章 §7 配置键清单落库）。</summary>
public sealed class SettingsRepository : ISettingsStore
{
    private readonly string _dbPath;

    public SettingsRepository(string dbPath) => _dbPath = dbPath;

    public async Task<string?> GetAsync(string key, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var row = await db.Settings.FindAsync(new object[] { key }, ct);
            return row?.Value;
        }, ct);

    public async Task SetAsync(string key, string value, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var existing = await db.Settings.FindAsync(new object[] { key }, ct);
            if (existing is null)
            {
                db.Settings.Add(new SettingEntity { Key = key, Value = value });
            }
            else
            {
                existing.Value = value;
            }

            await db.SaveChangesAsync(ct);
        }, ct);
}
