using MailHelper.Core.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MailHelper.Infrastructure.Storage;

/// <summary>notification_log 仓储（INotificationStore 实现；04 §3.2 表结构与 §8 去重/补发语义）。</summary>
public sealed class NotificationRepository : INotificationStore
{
    private readonly string _dbPath;

    public NotificationRepository(string dbPath) => _dbPath = dbPath;

    public async Task<IReadOnlyList<string>> FilterUnnotifiedAsync(IReadOnlyList<string> messageIds, CancellationToken ct)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        return await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var known = await db.NotificationLogs
                .Where(n => messageIds.Contains(n.MessageId))
                .Select(n => n.MessageId)
                .ToListAsync(ct);
            var knownSet = new HashSet<string>(known);
            return messageIds.Where(id => !knownSet.Contains(id)).ToList();
        }, ct);
    }

    public async Task LogRangeAsync(IReadOnlyList<NotificationLogEntry> entries, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var ids = entries.Select(e => e.MessageId).ToList();
            var existing = await db.NotificationLogs
                .Where(n => ids.Contains(n.MessageId))
                .Select(n => n.MessageId)
                .ToListAsync(ct);
            var existingSet = new HashSet<string>(existing);
            foreach (var entry in entries.DistinctBy(e => e.MessageId)) // 批内去重（同批重放亦幂等）
            {
                if (existingSet.Contains(entry.MessageId))
                {
                    continue; // 库中已存在（EX-05 续传重灌同批不重复）
                }

                existingSet.Add(entry.MessageId);
                db.NotificationLogs.Add(new NotificationLogEntity
                {
                    Id = Guid.NewGuid().ToString("N"),
                    MessageId = entry.MessageId,
                    Level = entry.Level,
                    SentAtUtc = entry.LoggedAtUtc.ToUnixTimeSeconds(),
                });
            }

            await db.SaveChangesAsync(ct);
        }, ct);

    public async Task<int> CountQueuedAsync(CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
            await db.NotificationLogs.CountAsync(n => n.Level == "queued", ct), ct);

    public async Task<int> FlushQueuedAsync(CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var queued = await db.NotificationLogs.Where(n => n.Level == "queued").ToListAsync(ct);
            if (queued.Count == 0)
            {
                return 0;
            }

            foreach (var row in queued)
            {
                row.Level = "flushed"; // 已随摘要补发（D-51）：保留参与去重，不再计入待补发
            }

            await db.SaveChangesAsync(ct);
            return queued.Count;
        }, ct);
}
