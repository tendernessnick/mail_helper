using MailHelper.Core.Domain;

namespace MailHelper.Core.Abstractions;

/// <summary>邮件事实表仓储（03 章 L3 存储扩展点；Infrastructure 以 EF Core 8 + SQLite 实现，FTS 原生 SQL 隔离在实现内）。</summary>
public interface IMessageStore
{
    /// <summary>按主键幂等 upsert（FR-04 AC2）：新行插入（other/P2 待分类）；已存在行仅更新同步字段，
    /// 分类五字段保留——EX-08：人工改判不被同步覆盖。返回新增行数。批 100/事务（04 §4.2）。</summary>
    Task<int> UpsertRangeAsync(string accountId, IReadOnlyList<MailMessage> messages, CancellationToken ct);

    /// <summary>取未分类邮件批次（classified_at_utc IS NULL），按接收时间升序。</summary>
    Task<IReadOnlyList<MailMessage>> GetPendingClassificationAsync(string accountId, int batchSize, CancellationToken ct);

    /// <summary>写回分类结果（S5 管线调用；S8 用户改判同入口，classifiedBy 区分）。</summary>
    Task ApplyClassificationRangeAsync(IReadOnlyList<ClassificationWrite> writes, CancellationToken ct);

    /// <summary>FTS5 全文检索（FR-13）：主题/发件人/正文预览；语法解析（from:/cat:/p:）在 S10 SearchService。</summary>
    Task<IReadOnlyList<MailMessage>> SearchFtsAsync(string accountId, string query, int limit, CancellationToken ct);
}

/// <summary>账户与同步断点仓储。</summary>
public interface IAccountStore
{
    Task UpsertAccountAsync(Account account, CancellationToken ct);

    Task<Account?> FindAccountAsync(string accountId, CancellationToken ct);

    Task<SyncCheckpoint?> GetCheckpointAsync(string accountId, CancellationToken ct);

    Task SaveCheckpointAsync(SyncCheckpoint checkpoint, CancellationToken ct);
}

/// <summary>键值设置仓储（04 章 §7 配置项清单的 settings 表部分）。</summary>
public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, string value, CancellationToken ct);
}

/// <summary>正文磁盘缓存（03 章 §6：bodies\{accountId}\{sha1(html)}.html，LRU 上限默认 2GB）。</summary>
public interface IBodyCache
{
    /// <summary>保存正文，返回相对路径（{accountId}/{sha1}.html）。</summary>
    Task<string> SaveAsync(string accountId, string html, CancellationToken ct);

    /// <summary>读取正文（同时刷新 LRU 访问时间）；不存在返回 null。</summary>
    Task<string?> ReadAsync(string relativePath, CancellationToken ct);
}
