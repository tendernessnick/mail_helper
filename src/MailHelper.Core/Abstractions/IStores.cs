using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;

namespace MailHelper.Core.Abstractions;

/// <summary>收件箱查询（FR-12：类别/重要度/未读过滤 + 待确认队列入口）。</summary>
public sealed record InboxQuery(
    string? Category = null,
    bool NeedsReviewOnly = false,
    Importance? MinimumImportance = null,
    bool UnreadOnly = false,
    double ReviewThreshold = 0.55,
    int Limit = 200);

/// <summary>组合搜索查询（S10 FR-13：语法解析产物）。</summary>
public sealed record SearchQuery(
    string FreeText,
    string? FromFilter = null,
    string? Category = null,
    Importance? Importance = null)
{
    public bool IsEmpty =>
        FreeText.Length == 0 && FromFilter is null && Category is null && Importance is null;
}

/// <summary>邮件事实表仓储（03 章 L3 存储扩展点；Infrastructure 以 EF Core 8 + SQLite 实现，FTS 原生 SQL 隔离在实现内）。</summary>
public interface IMessageStore
{
    /// <summary>按主键幂等 upsert（FR-04 AC2）：新行插入（other/P2 待分类）；已存在行仅更新同步字段，
    /// 分类五字段保留——EX-08：人工改判不被同步覆盖。返回新增行数。批 100/事务（04 §4.2）。</summary>
    Task<int> UpsertRangeAsync(string accountId, IReadOnlyList<MailMessage> messages, CancellationToken ct);

    /// <summary>按主键取单封（S8 改判前置：读旧类别/旧重要度/发件人）。</summary>
    Task<MailMessage?> GetByIdAsync(string messageId, CancellationToken ct);

    /// <summary>取未分类邮件批次（classified_at_utc IS NULL），按接收时间升序。</summary>
    Task<IReadOnlyList<MailMessage>> GetPendingClassificationAsync(string accountId, int batchSize, CancellationToken ct);

    /// <summary>写回分类结果（S5 管线调用；S8 用户改判同入口，classifiedBy 区分）。</summary>
    Task ApplyClassificationRangeAsync(IReadOnlyList<ClassificationWrite> writes, CancellationToken ct);

    /// <summary>S13-B 规则包版本迁移：把机器来源（classifiedBy≠'user'，如 rule-engine/llm）的已分类邮件
    /// 置回待处理（classified_at/confidence 清空、类别与重要度复位默认）；用户改判永不触碰。
    /// 返回重置行数。</summary>
    Task<int> ResetRuleClassificationAsync(string accountId, CancellationToken ct);

    /// <summary>FTS5 全文检索（FR-13）：主题/发件人/正文预览；语法解析（from:/cat:/p:）在 S10 SearchService。</summary>
    Task<IReadOnlyList<MailMessage>> SearchFtsAsync(string accountId, string query, int limit, CancellationToken ct);

    /// <summary>组合搜索（S10）：自由文本（FTS 双路径）+ 发件人子串/类别/重要度过滤；空文本=纯过滤默认排序。</summary>
    Task<IReadOnlyList<MailMessage>> SearchAsync(string accountId, SearchQuery query, int limit, CancellationToken ct);

    /// <summary>收件箱列表查询（FR-12）：默认排序 importance DESC → received DESC，排除远端已删。</summary>
    Task<IReadOnlyList<MailMessage>> GetInboxAsync(string accountId, InboxQuery query, CancellationToken ct);

    /// <summary>各类别未读数（左栏徽章）。</summary>
    Task<IReadOnlyDictionary<string, int>> GetUnreadCountsAsync(string accountId, CancellationToken ct);

    /// <summary>待确认队列计数（FR-09 侧栏入口红点）。</summary>
    Task<int> GetNeedsReviewCountAsync(string accountId, double reviewThreshold, CancellationToken ct);

    /// <summary>标记已读（仅本地，UC-04 后置条件）。</summary>
    Task MarkReadAsync(string messageId, CancellationToken ct);

    /// <summary>S18 DDL 提取候选（CHG-015）：Canvas 发件域（instructure.com / canvas.*）且
    /// ddl_scanned_at_utc IS NULL 的邮件，按接收时间升序——天然支持首启用回填历史（ADR-006）。</summary>
    Task<IReadOnlyList<MailMessage>> GetDdlCandidatesAsync(string accountId, int batchSize, CancellationToken ct);

    /// <summary>S18：批量置 ddl_scanned_at_utc——扫过即置位（无论是否解析出条目），保证不重扫（AC5）。</summary>
    Task MarkDdlScannedAsync(IReadOnlyList<string> messageIds, DateTime scannedAtUtc, CancellationToken ct);
}

/// <summary>账户与同步断点仓储。</summary>
public interface IAccountStore
{
    Task UpsertAccountAsync(Account account, CancellationToken ct);

    Task<Account?> FindAccountAsync(string accountId, CancellationToken ct);

    /// <summary>全部账户（v1 单账户；V1.2 多账户演进预留）。</summary>
    Task<IReadOnlyList<Account>> FindAllAsync(CancellationToken ct);

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

/// <summary>用户/反馈规则仓储（rules 表；04 §3.2 DDL。内置规则包仍走 JSON 只读层，D-40）。</summary>
public interface IRulesStore
{
    /// <summary>全部规则行（含 Builtin 行；启用过滤交由引擎/调用方）。</summary>
    Task<IReadOnlyList<ClassifyRule>> GetAllAsync(CancellationToken ct);

    /// <summary>按 Id 幂等 upsert（FR-11：反馈生成的发件人规则）。</summary>
    Task UpsertAsync(ClassifyRule rule, CancellationToken ct);

    /// <summary>按 Id 删除（S9：用户规则删除 / 反馈规则撤销学习；不存在时 no-op）。</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>改判反馈仓储（classification_feedback 表；S8 写入 + 计数，历史查看随 S9 规则管理页）。</summary>
public interface IFeedbackStore
{
    Task AddAsync(ClassificationFeedback feedback, CancellationToken ct);

    Task<int> CountAsync(CancellationToken ct);
}

/// <summary>自定义类别仓储（S14-C/CHG-012；内置七类为代码静态不落库，此处只管用户自定义）。</summary>
public interface ICategoryStore
{
    /// <summary>全部自定义类别（Sort 升序）。</summary>
    Task<IReadOnlyList<CategoryDefinition>> GetAllAsync(CancellationToken ct);

    /// <summary>新建或更新自定义类别（Id 为 slug，内置 ID 拒绝）。</summary>
    Task UpsertAsync(CategoryDefinition category, CancellationToken ct);

    /// <summary>删除自定义类别：该类邮件归「其他」、相关规则一并删除（事务）。返回迁移的邮件数。</summary>
    Task<int> DeleteAsync(string id, CancellationToken ct);
}

/// <summary>日程条目仓储（S18/CHG-015：schedule_items 表；(account_id, dedupe_key) 唯一）。</summary>
public interface IScheduleStore
{
    /// <summary>按去重键幂等 upsert：新键插入；已有键且 due 变化 → 更新 due/来源/时间戳并清提醒标记；
    /// due 未变仅刷新来源指向。返回 (条目, 是否新增, due 是否更新)。</summary>
    Task<(ScheduleItem Item, bool Inserted, bool DueUpdated)> UpsertByDedupeKeyAsync(
        ScheduleItem item, CancellationToken ct);

    /// <summary>全部条目（open 按截止升序在前，Done/Ignored 按截止降序垫底——日程页直接渲染）。</summary>
    Task<IReadOnlyList<ScheduleItem>> GetAllAsync(string accountId, CancellationToken ct);

    /// <summary>未完成条目数（导航徽章/汇总文案）。</summary>
    Task<int> CountOpenAsync(string accountId, CancellationToken ct);

    Task SetStatusAsync(string itemId, ScheduleItemStatus status, CancellationToken ct);

    /// <summary>登记已提醒（记录提醒时的 due 值：due 变更后自动恢复提醒资格）。</summary>
    Task MarkRemindedAsync(string itemId, long remindedDueAtUtc, CancellationToken ct);

    /// <summary>清理终态（Done/Ignored）且 updated 早于 cutoff 的条目。返回删除数。</summary>
    Task<int> DeleteFinishedBeforeAsync(DateTime cutoffUtc, CancellationToken ct);
}
