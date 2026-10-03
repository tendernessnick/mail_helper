using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace MailHelper.Infrastructure.Storage;

/// <summary>schedule_items 仓储（IScheduleStore 实现，S18/CHG-015；(account_id, dedupe_key) 唯一防重；
/// upsert 语义见接口注释——due 变化清提醒标记，使变更后的截止时间可再次提醒）。</summary>
public sealed class ScheduleRepository : IScheduleStore
{
    private readonly string _dbPath;

    public ScheduleRepository(string dbPath) => _dbPath = dbPath;

    public async Task<(ScheduleItem Item, bool Inserted, bool DueUpdated)> UpsertByDedupeKeyAsync(
        ScheduleItem item, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var now = MailRepository.ToUnixSeconds(DateTime.UtcNow);
            var row = await db.ScheduleItems.FirstOrDefaultAsync(
                s => s.AccountId == item.AccountId && s.DedupeKey == item.DedupeKey, ct);
            if (row is null)
            {
                var entity = new ScheduleItemEntity
                {
                    Id = string.IsNullOrEmpty(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
                    AccountId = item.AccountId,
                    MessageId = item.MessageId,
                    Source = item.Source,
                    Title = item.Title,
                    Course = item.Course,
                    CourseCode = item.CourseCode,
                    Link = item.Link,
                    DedupeKey = item.DedupeKey,
                    DueAtUtc = MailRepository.ToUnixSeconds(item.DueAtUtc),
                    Status = (int)item.Status,
                    RemindedDueAt = item.RemindedDueAt,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.ScheduleItems.Add(entity);
                await db.SaveChangesAsync(ct);
                return (ToDomain(entity), true, false);
            }

            var dueUpdated = false;
            var newDue = MailRepository.ToUnixSeconds(item.DueAtUtc);
            if (row.DueAtUtc != newDue)
            {
                row.DueAtUtc = newDue;
                row.RemindedDueAt = null; // due 变更 → 恢复提醒资格（US-18-2）
                row.UpdatedAtUtc = now;
                dueUpdated = true;
            }

            if (!string.IsNullOrEmpty(item.MessageId) && row.MessageId != item.MessageId)
            {
                row.MessageId = item.MessageId; // 来源指向最近一次提及（摘要周报反复刷新）
                if (!dueUpdated)
                {
                    row.UpdatedAtUtc = now;
                }
            }

            if (item.Link is { Length: > 0 } && row.Link != item.Link)
            {
                row.Link = item.Link;
            }

            await db.SaveChangesAsync(ct);
            return (ToDomain(row), false, dueUpdated);
        }, ct);

    public async Task<IReadOnlyList<ScheduleItem>> GetAllAsync(string accountId, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var open = await db.ScheduleItems.AsNoTracking()
                .Where(s => s.AccountId == accountId && s.Status == (int)ScheduleItemStatus.Open)
                .OrderBy(s => s.DueAtUtc)
                .ToListAsync(ct); // 未完成按截止升序（最紧急在最前）
            var finished = await db.ScheduleItems.AsNoTracking()
                .Where(s => s.AccountId == accountId && s.Status != (int)ScheduleItemStatus.Open)
                .OrderByDescending(s => s.DueAtUtc)
                .ToListAsync(ct); // 完成/忽略垫底（最近处理的在前）
            var all = open.Concat(finished).Select(ToDomain).ToList();
            return (IReadOnlyList<ScheduleItem>)all;
        }, ct);

    public async Task<int> CountOpenAsync(string accountId, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
            await db.ScheduleItems.AsNoTracking()
                .CountAsync(s => s.AccountId == accountId && s.Status == (int)ScheduleItemStatus.Open, ct), ct);

    public async Task SetStatusAsync(string itemId, ScheduleItemStatus status, CancellationToken ct)
    {
        var now = MailRepository.ToUnixSeconds(DateTime.UtcNow); // 表达式外求值（EF 不可翻译方法调用）
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            await db.ScheduleItems
                .Where(s => s.Id == itemId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, (int)status)
                    .SetProperty(e => e.UpdatedAtUtc, now), ct);
        }, ct);
    }

    public async Task MarkRemindedAsync(string itemId, long remindedDueAtUtc, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            await db.ScheduleItems
                .Where(s => s.Id == itemId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.RemindedDueAt, remindedDueAtUtc), ct);
        }, ct);

    public async Task<int> DeleteFinishedBeforeAsync(DateTime cutoffUtc, CancellationToken ct)
    {
        var cutoff = MailRepository.ToUnixSeconds(cutoffUtc); // 表达式外求值（EF 不可翻译方法调用）
        return await MailDatabase.WithDbAsync(_dbPath, async db =>
            await db.ScheduleItems
                .Where(s => s.Status != (int)ScheduleItemStatus.Open && s.UpdatedAtUtc < cutoff)
                .ExecuteDeleteAsync(ct), ct);
    }

    internal static ScheduleItem ToDomain(ScheduleItemEntity e) => new(
        e.Id,
        e.AccountId,
        e.MessageId,
        e.Source,
        e.Title,
        e.Course,
        e.CourseCode,
        e.Link,
        e.DedupeKey,
        MailRepository.FromUnixSeconds(e.DueAtUtc),
        (ScheduleItemStatus)e.Status,
        e.RemindedDueAt,
        MailRepository.FromUnixSeconds(e.CreatedAtUtc),
        MailRepository.FromUnixSeconds(e.UpdatedAtUtc));
}
