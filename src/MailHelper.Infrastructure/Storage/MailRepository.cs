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

        // 所有过滤条件先进入 SQL，再排序/分页。不能先截取 FTS 候选再过滤：
        // 稀疏类别会漏掉排在候选窗口之外的结果，也不能把 limit*4 当成实际返回上限。
        if (sanitized.Length >= 3)
        {
            try
            {
                return await SearchTextAsync(accountId, sanitized, query, limit, useMatch: true, ct);
            }
            catch (SqliteException)
            {
            }
        }

        return await SearchTextAsync(accountId, sanitized, query, limit, useMatch: false, ct);
    }

    private static IQueryable<MessageEntity> ApplyFilters(IQueryable<MessageEntity> rows, SearchQuery query)
    {
        if (query.FromFilter is { } from)
        {
            var pattern = "%" + EscapeLike(from) + "%";
            rows = rows.Where(m => m.FromAddress != null && EF.Functions.Like(m.FromAddress, pattern, "\\"));
        }

        if (query.Category is { } category)
        {
            rows = rows.Where(m => m.Category == category);
        }

        if (query.Importance is { } importance)
        {
            rows = rows.Where(m => m.Importance == (int)importance);
        }

        return ApplyInboxFilters(rows, query.Scope ?? new InboxQuery());
    }

    private static IQueryable<MessageEntity> ApplyInboxFilters(IQueryable<MessageEntity> rows, InboxQuery query)
    {
        if (query.Category is { } category)
        {
            rows = rows.Where(m => m.Category == category);
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

        return rows;
    }

    private async Task<IReadOnlyList<MailMessage>> FilteredAsync(string accountId, SearchQuery query, int limit, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var rows = db.Messages.AsNoTracking()
                .Where(m => m.AccountId == accountId && !m.IsDeletedRemote);
            rows = ApplyFilters(rows, query);
            var ordered = query.Scope?.Sort == InboxSortMode.Date
                ? rows.OrderByDescending(m => m.ReceivedAtUtc).ThenByDescending(m => m.Importance)
                : rows.OrderByDescending(m => m.Importance).ThenByDescending(m => m.ReceivedAtUtc);
            var list = await ordered.ThenBy(m => m.Id)
                .Skip(Math.Max(0, query.Scope?.Offset ?? 0))
                .Take(limit)
                .ToListAsync(ct);
            return (IReadOnlyList<MailMessage>)list.Select(ToDomain).ToList();
        }, ct);

    public async Task<IReadOnlyList<MailMessage>> GetInboxAsync(string accountId, InboxQuery query, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var rows = db.Messages.AsNoTracking()
                .Where(m => m.AccountId == accountId && !m.IsDeletedRemote);

            rows = ApplyInboxFilters(rows, query);

            // S19/CHG-016：排序模式二选一——重要度优先（默认，历史行为）/ 时间优先（同日内重要度递减）
            var ordered = query.Sort == InboxSortMode.Date
                ? rows.OrderByDescending(m => m.ReceivedAtUtc).ThenByDescending(m => m.Importance)
                : rows.OrderByDescending(m => m.Importance).ThenByDescending(m => m.ReceivedAtUtc);
            rows = ordered.ThenBy(m => m.Id)
                .Skip(Math.Max(0, query.Offset)).Take(Math.Max(0, query.Limit));

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
                result[group.Category] = group.Count; // 自定义分类同样计入侧栏和全局未读。
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

    /// <summary>S18 DDL 候选（CHG-015）：Canvas 发件域 + 未扫描置位，接收时间升序（回填历史）。</summary>
    public async Task<IReadOnlyList<MailMessage>> GetDdlCandidatesAsync(string accountId, int batchSize, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var rows = await db.Messages.AsNoTracking()
                .Where(m => m.AccountId == accountId
                    && !m.IsDeletedRemote
                    && m.DdlScannedAtUtc == null
                    && (m.FromAddress != null && (m.FromAddress.Contains("instructure.com") || m.FromAddress.Contains("canvas."))))
                .OrderBy(m => m.ReceivedAtUtc)
                .Take(batchSize)
                .ToListAsync(ct);
            return (IReadOnlyList<MailMessage>)rows.Select(ToDomain).ToList();
        }, ct);

    public async Task MarkDdlScannedAsync(IReadOnlyList<string> messageIds, DateTime scannedAtUtc, CancellationToken ct)
    {
        if (messageIds.Count == 0)
        {
            return;
        }

        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var seconds = ToUnixSeconds(scannedAtUtc);
            await db.Messages
                .Where(m => messageIds.Contains(m.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.DdlScannedAtUtc, seconds), ct);
        }, ct);
    }

    private Task<IReadOnlyList<MailMessage>> SearchByMatchAsync(string accountId, string query, int limit, CancellationToken ct) =>
        SearchTextAsync(accountId, query, new SearchQuery(query), limit, useMatch: true, ct);

    private Task<IReadOnlyList<MailMessage>> SearchByLikeAsync(string accountId, string query, int limit, CancellationToken ct) =>
        SearchTextAsync(accountId, query, new SearchQuery(query), limit, useMatch: false, ct);

    private async Task<IReadOnlyList<MailMessage>> SearchTextAsync(
        string accountId, string text, SearchQuery query, int limit, bool useMatch, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var scope = query.Scope;
            // SQL 片段只在两个常量分支间选择，用户输入全部走 SQLite 参数。
            var join = useMatch
                ? "JOIN (SELECT rowid, rank FROM messages_fts WHERE messages_fts MATCH $text) AS f ON f.rowid = m.rowid"
                : "JOIN messages_fts AS f ON f.rowid = m.rowid";
            var textFilter = useMatch ? string.Empty : """
                  AND (m.subject LIKE $text ESCAPE '\'
                       OR m.from_name LIKE $text ESCAPE '\'
                       OR m.body_preview LIKE $text ESCAPE '\')
                """;
            var order = scope is null
                ? useMatch ? "f.rank, m.id ASC" : "m.received_at_utc DESC, m.id ASC"
                : scope.Sort == InboxSortMode.Date
                    ? "m.received_at_utc DESC, m.importance DESC, m.id ASC"
                    : "m.importance DESC, m.received_at_utc DESC, m.id ASC";
            var sql = $"""
                    SELECT m.*
                    FROM messages AS m
                    {join}
                    WHERE m.account_id = $account AND m.is_deleted_remote = 0
                      AND ($from = '' OR m.from_address LIKE $from ESCAPE '\')
                      AND ($category = '' OR m.category = $category)
                      AND ($scopeCategory = '' OR m.category = $scopeCategory)
                      AND ($importance < 0 OR m.importance = $importance)
                      AND ($minimumImportance < 0 OR m.importance >= $minimumImportance)
                      AND ($unread = 0 OR m.is_read = 0)
                      AND ($review = 0 OR (m.classified_at_utc IS NOT NULL AND m.confidence < $threshold))
                      {textFilter}
                    ORDER BY {order}
                    LIMIT $limit OFFSET $offset
                    """;
            object[] parameters =
            [
                new SqliteParameter("$account", accountId),
                new SqliteParameter("$text", useMatch ? "\"" + text + "\"" : "%" + EscapeLike(text) + "%"),
                new SqliteParameter("$from", query.FromFilter is { } from ? "%" + EscapeLike(from) + "%" : string.Empty),
                new SqliteParameter("$category", query.Category ?? string.Empty),
                new SqliteParameter("$scopeCategory", scope?.Category ?? string.Empty),
                new SqliteParameter("$importance", query.Importance is { } importance ? (int)importance : -1),
                new SqliteParameter("$minimumImportance", scope?.MinimumImportance is { } min ? (int)min : -1),
                new SqliteParameter("$unread", scope?.UnreadOnly == true ? 1 : 0),
                new SqliteParameter("$review", scope?.NeedsReviewOnly == true ? 1 : 0),
                new SqliteParameter("$threshold", scope?.ReviewThreshold ?? 0.55),
                new SqliteParameter("$limit", limit),
                new SqliteParameter("$offset", Math.Max(0, scope?.Offset ?? 0)),
            ];
            var rows = await db.Messages.FromSqlRaw(sql, parameters)
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
