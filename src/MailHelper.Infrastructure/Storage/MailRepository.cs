using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MailHelper.Infrastructure.Storage;

/// <summary>邮件事实表仓储（IMessageStore 的 EF Core 8 + SQLite 实现；FTS 原生 SQL 按 ADR-003 隔离在此）。</summary>
public sealed class MailRepository : IMessageStore
{
    private const int BatchSize = 100; // 04 §4.2：批量入库批 100/事务

    private readonly string _dbPath;

    public MailRepository(string dbPath) => _dbPath = dbPath;

    public async Task<int> UpsertRangeAsync(string accountId, IReadOnlyList<MailMessage> messages, CancellationToken ct)
    {
        var inserted = 0;
        for (var offset = 0; offset < messages.Count; offset += BatchSize)
        {
            var batch = messages.Skip(offset).Take(BatchSize).ToList();
            inserted += await MailDatabase.WithDbAsync(_dbPath, async db =>
            {
                var ids = batch.Select(m => m.Id).ToList();
                var existing = await db.Messages
                    .Where(m => ids.Contains(m.Id))
                    .ToDictionaryAsync(m => m.Id, ct);

                var batchInserted = 0;
                foreach (var message in batch)
                {
                    if (existing.TryGetValue(message.Id, out var row))
                    {
                        UpdateSyncFields(row, message); // 分类五字段保留（EX-08：改判优先）
                    }
                    else
                    {
                        db.Messages.Add(ToEntity(message));
                        batchInserted++;
                    }
                }

                await db.SaveChangesAsync(ct);
                return batchInserted;
            }, ct);
        }

        return inserted;
    }

    public async Task<IReadOnlyList<MailMessage>> GetPendingClassificationAsync(string accountId, int batchSize, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var rows = await db.Messages
                .Where(m => m.AccountId == accountId && m.ClassifiedAtUtc == null)
                .OrderBy(m => m.ReceivedAtUtc)
                .Take(batchSize)
                .AsNoTracking()
                .ToListAsync(ct);
            return (IReadOnlyList<MailMessage>)rows.Select(ToDomain).ToList();
        }, ct);

    public async Task<MailMessage?> GetByIdAsync(string messageId, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var row = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, ct);
            return row is null ? null : ToDomain(row);
        }, ct);

    public async Task ApplyClassificationRangeAsync(IReadOnlyList<ClassificationWrite> writes, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var ids = writes.Select(w => w.MessageId).ToList();
            var rows = await db.Messages
                .Where(m => ids.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, ct);

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var write in writes)
            {
                if (rows.TryGetValue(write.MessageId, out var row))
                {
                    row.Category = CategoryToString(write.Category);
                    row.Importance = (int)write.Importance;
                    row.Confidence = write.Confidence;
                    row.ClassifiedBy = write.ClassifiedBy;
                    row.ClassifiedAtUtc = now;
                }
            }

            await db.SaveChangesAsync(ct);
        }, ct);

    public async Task<int> ResetRuleClassificationAsync(string accountId, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            // S13-B：仅机器来源回炉（rule-engine/llm/…），用户改判（'user'）与反馈学习结果不触碰；
            // 分类管线按 classified_at IS NULL 重取
            var rows = await db.Messages
                .Where(m => m.AccountId == accountId
                    && m.ClassifiedBy != "user"
                    && m.ClassifiedAtUtc != null)
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                row.Category = CategoryIds.Other;
                row.Importance = (int)Importance.P2;
                row.Confidence = null;
                row.ClassifiedAtUtc = null;
            }

            await db.SaveChangesAsync(ct);
            return rows.Count;
        }, ct);

    public async Task<IReadOnlyList<MailMessage>> SearchFtsAsync(string accountId, string query, int limit, CancellationToken ct)
    {
        var sanitized = Sanitize(query);
        if (sanitized.Length == 0 || limit <= 0)
        {
            return Array.Empty<MailMessage>();
        }

        // D-22：查询词 ≥3 字符走 trigram MATCH（rank 排序）；
        // <3 字符（如中文双字词「学费」）或 MATCH 异常时走 LIKE 兜底（正确性不依赖索引）
        if (sanitized.Length >= 3)
        {
            try
            {
                return await SearchByMatchAsync(accountId, sanitized, limit, ct);
            }
            catch (SqliteException)
            {
                // 落入 LIKE 兜底
            }
        }

        return await SearchByLikeAsync(accountId, sanitized, limit, ct);
    }

    /// <summary>组合搜索（S10 FR-13）：自由文本走 FTS 双路径（D-21/D-22）+ from 子串/类别/重要度 LINQ 过滤；
    /// 纯过滤（无文本）走默认排序（重要度→时间，与收件箱一致）。FTS 路径含 rank 排序。</summary>
    public async Task<IReadOnlyList<MailMessage>> SearchAsync(string accountId, SearchQuery query, int limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            return Array.Empty<MailMessage>();
        }

        if (query.FreeText.Length == 0)
        {
            return await FilteredAsync(accountId, query, limit, ct);
        }

        var sanitized = Sanitize(query.FreeText);
        if (sanitized.Length == 0)
        {
            return await FilteredAsync(accountId, query with { FreeText = string.Empty }, limit, ct);
        }

        // 文本 + 过滤组合：先 FTS 命中再 LINQ 过滤（万级下 FTS 索引先收敛，PERF-03 P95<500ms）
        if (sanitized.Length >= 3)
        {
            try
            {
                var matched = await SearchByMatchAsync(accountId, sanitized, limit * 4, ct); // 放大候选再过滤
                var filtered = ApplyFilters(matched, query).ToList();
                if (filtered.Count > 0 || !HasFilters(query))
                {
                    return filtered;
                }
                // 全被过滤掉：可能是 rank 截断（limit*4），落到 LIKE 兜底再试
            }
            catch (SqliteException)
            {
            }
        }

        var likeHits = await SearchByLikeAsync(accountId, sanitized, limit * 4, ct);
        return ApplyFilters(likeHits, query).ToList();
    }

    private static bool HasFilters(SearchQuery query) =>
        query.FromFilter is not null || query.Category is not null || query.Importance is not null;

    private static IEnumerable<MailMessage> ApplyFilters(IEnumerable<MailMessage> mails, SearchQuery query)
    {
        foreach (var mail in mails)
        {
            if (query.FromFilter is { } from && (mail.FromAddress is null || !mail.FromAddress.Contains(from, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (query.Category is { } category && mail.Category != category)
            {
                continue;
            }

            if (query.Importance is { } importance && mail.Importance != importance)
            {
                continue;
            }

            yield return mail;
        }
    }

    private async Task<IReadOnlyList<MailMessage>> FilteredAsync(string accountId, SearchQuery query, int limit, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var rows = db.Messages.AsNoTracking()
                .Where(m => m.AccountId == accountId && !m.IsDeletedRemote);
            if (query.FromFilter is { } from)
            {
                rows = rows.Where(m => m.FromAddress!.Contains(from));
            }

            if (query.Category is { } category)
            {
                rows = rows.Where(m => m.Category == CategoryToString(category));
            }

            if (query.Importance is { } importance)
            {
                rows = rows.Where(m => m.Importance == (int)importance);
            }

            var list = await rows
                .OrderByDescending(m => m.Importance)
                .OrderByDescending(m => m.ReceivedAtUtc)
                .Take(limit)
                .ToListAsync(ct);
            return (IReadOnlyList<MailMessage>)list.Select(ToDomain).ToList();
        }, ct);

    public async Task<IReadOnlyList<MailMessage>> GetInboxAsync(string accountId, InboxQuery query, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var rows = db.Messages.AsNoTracking()
                .Where(m => m.AccountId == accountId && !m.IsDeletedRemote);

            if (query.Category is { } category)
            {
                var categoryText = CategoryToString(category);
                rows = rows.Where(m => m.Category == categoryText);
            }

            if (query.UnreadOnly)
            {
                rows = rows.Where(m => !m.IsRead);
            }

            if (query.MinimumImportance is { } importance)
            {
                rows = rows.Where(m => m.Importance >= (int)importance);
            }

            if (query.NeedsReviewOnly)
            {
                rows = rows.Where(m => m.ClassifiedAtUtc != null && m.Confidence < query.ReviewThreshold);
            }

            rows = rows
                .OrderByDescending(m => m.Importance)
                .ThenByDescending(m => m.ReceivedAtUtc)
                .Take(query.Limit);

            var list = await rows.ToListAsync(ct);
            return (IReadOnlyList<MailMessage>)list.Select(ToDomain).ToList();
        }, ct);

    public async Task<IReadOnlyDictionary<string, int>> GetUnreadCountsAsync(string accountId, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var groups = await db.Messages.AsNoTracking()
                .Where(m => m.AccountId == accountId && !m.IsDeletedRemote && !m.IsRead)
                .GroupBy(m => m.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var result = CategoryIds.All.ToDictionary(c => c, _ => 0);
            foreach (var group in groups)
            {
                if (result.ContainsKey(group.Category))
                {
                    result[group.Category] = group.Count;
                }
            }

            return (IReadOnlyDictionary<string, int>)result;
        }, ct);

    public async Task<int> GetNeedsReviewCountAsync(string accountId, double reviewThreshold, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
            await db.Messages.AsNoTracking()
                .CountAsync(m => m.AccountId == accountId
                    && !m.IsDeletedRemote
                    && m.ClassifiedAtUtc != null
                    && m.Confidence < reviewThreshold, ct), ct);

    public async Task MarkReadAsync(string messageId, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var row = await db.Messages.FindAsync(new object[] { messageId }, ct);
            if (row is not null && !row.IsRead)
            {
                row.IsRead = true;
                await db.SaveChangesAsync(ct);
            }
        }, ct);

    private async Task<IReadOnlyList<MailMessage>> SearchByMatchAsync(string accountId, string query, int limit, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var phrase = "\"" + query + "\"";
            var rows = await db.Messages.FromSql($"""
                    SELECT m.*
                    FROM messages AS m
                    JOIN (SELECT rowid, rank FROM messages_fts WHERE messages_fts MATCH {phrase}) AS f
                        ON f.rowid = m.rowid
                    WHERE m.account_id = {accountId}
                    ORDER BY f.rank
                    LIMIT {limit}
                    """)
                .AsNoTracking()
                .ToListAsync(ct);
            return (IReadOnlyList<MailMessage>)rows.Select(ToDomain).ToList();
        }, ct);

    private async Task<IReadOnlyList<MailMessage>> SearchByLikeAsync(string accountId, string query, int limit, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var pattern = "%" + EscapeLike(query) + "%";
            var rows = await db.Messages.FromSql($"""
                    SELECT m.*
                    FROM messages AS m
                    JOIN messages_fts AS f ON f.rowid = m.rowid
                    WHERE m.account_id = {accountId}
                      AND (m.subject LIKE {pattern} ESCAPE '\'
                           OR m.from_name LIKE {pattern} ESCAPE '\'
                           OR m.body_preview LIKE {pattern} ESCAPE '\')
                    ORDER BY m.received_at_utc DESC
                    LIMIT {limit}
                    """)
                .AsNoTracking()
                .ToListAsync(ct);
            return (IReadOnlyList<MailMessage>)rows.Select(ToDomain).ToList();
        }, ct);

    /// <summary>去除 FTS 语法字符（引号/通配符），防 MATCH 语法注入（SEC-04 意识）。</summary>
    private static string Sanitize(string query) => query.Replace("\"", string.Empty).Replace("*", string.Empty).Trim();

    private static string EscapeLike(string query) =>
        query.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static void UpdateSyncFields(MessageEntity row, MailMessage m)
    {
        row.InternetMessageId = m.InternetMessageId;
        row.Subject = m.Subject;
        row.FromName = m.FromName;
        row.FromAddress = m.FromAddress;
        row.BodyPreview = m.BodyPreview;
        row.BodyPath = m.BodyPath;
        row.ReceivedAtUtc = ToUnixSeconds(m.ReceivedAtUtc);
        row.HasAttachments = m.HasAttachments;
        row.IsRead = m.IsRead;
        row.RemoteChangeKey = m.RemoteChangeKey;
        row.IsDeletedRemote = m.IsDeletedRemote;
    }

    internal static MessageEntity ToEntity(MailMessage m) => new()
    {
        Id = m.Id,
        AccountId = m.AccountId,
        InternetMessageId = m.InternetMessageId,
        Subject = m.Subject,
        FromName = m.FromName,
        FromAddress = m.FromAddress,
        BodyPreview = m.BodyPreview,
        BodyPath = m.BodyPath,
        ReceivedAtUtc = ToUnixSeconds(m.ReceivedAtUtc),
        HasAttachments = m.HasAttachments,
        IsRead = m.IsRead,
        Category = CategoryToString(m.Category),
        Importance = (int)m.Importance,
        Confidence = m.Confidence,
        ClassifiedBy = m.ClassifiedBy,
        ClassifiedAtUtc = m.ClassifiedAtUtc is { } at ? ToUnixSeconds(at) : null,
        RemoteChangeKey = m.RemoteChangeKey,
        IsDeletedRemote = m.IsDeletedRemote,
    };

    internal static MailMessage ToDomain(MessageEntity e) => new(
        e.Id,
        e.AccountId,
        e.InternetMessageId,
        e.Subject,
        e.FromName,
        e.FromAddress,
        e.BodyPreview,
        e.BodyPath,
        FromUnixSeconds(e.ReceivedAtUtc),
        e.HasAttachments,
        e.IsRead,
        e.Category, // S14-C：类别即 ID
        (Importance)e.Importance,
        e.Confidence,
        e.ClassifiedBy,
        e.ClassifiedAtUtc is { } at ? FromUnixSeconds(at) : null,
        e.RemoteChangeKey,
        e.IsDeletedRemote);

    // S14-C：类别即 ID 字符串（DB 存量值与内置 ID 一致，零转换；未知自定义 ID 原样保留）
    internal static string CategoryToString(string category) => category;

    internal static long ToUnixSeconds(DateTime utc) =>
        new DateTimeOffset(utc.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();

    internal static DateTime FromUnixSeconds(long seconds) =>
        DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
}
