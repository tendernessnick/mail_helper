// EF Core 实体（04 章 §3.1 ER / §3.2 DDL 逐列对应）。
// 约定（D-23）：时间列一律 long Unix 秒；messages.category/importance 用 string/int 原始形态，
// 领域枚举转换隔离在仓储层——保持实体与 DDL 一字不差。
namespace MailHelper.Infrastructure.Storage;

internal sealed class AccountEntity
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? TenantId { get; set; }
    public string Channel { get; set; } = "Graph";
    public string? AzureClientId { get; set; }
    public string Status { get; set; } = "Active";
    public long CreatedAtUtc { get; set; }
}

internal sealed class SyncStateEntity
{
    public string AccountId { get; set; } = string.Empty;
    public string? DeltaLink { get; set; }
    public string? ImapUidWatermark { get; set; }
    public long? LastSyncAtUtc { get; set; }
    public string? LastSyncStatus { get; set; }
}

internal sealed class MessageEntity
{
    public string Id { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string? InternetMessageId { get; set; }
    public string? Subject { get; set; }
    public string? FromName { get; set; }
    public string? FromAddress { get; set; }
    public string? BodyPreview { get; set; }
    public string? BodyPath { get; set; }
    public long ReceivedAtUtc { get; set; }
    public bool HasAttachments { get; set; }
    public bool IsRead { get; set; }
    public string Category { get; set; } = "other";
    public int Importance { get; set; } = 2;   // DDL DEFAULT 2 原样保留（CHG-002：代码显式写值）
    public double? Confidence { get; set; }
    public string ClassifiedBy { get; set; } = "rule";
    public long? ClassifiedAtUtc { get; set; }
    public string? RemoteChangeKey { get; set; }
    public bool IsDeletedRemote { get; set; }

    /// <summary>S18/CHG-015：DDL 提取扫描置位时间（null=待扫描；扫过即置位，与是否解析出条目无关）。</summary>
    public long? DdlScannedAtUtc { get; set; }
}

internal sealed class RuleEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int? ImportanceHint { get; set; }
    public double Weight { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public string Source { get; set; } = "Builtin";
    public string? BuiltinVersion { get; set; }
    public long? UpdatedAtUtc { get; set; }
}

internal sealed class ClassificationFeedbackEntity
{
    public string Id { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string? OldCategory { get; set; }
    public string? NewCategory { get; set; }
    public int? OldImportance { get; set; }
    public int? NewImportance { get; set; }
    public long CreatedAtUtc { get; set; }
}

internal sealed class NotificationLogEntity
{
    public string Id { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Level { get; set; } = "P0";
    public long SentAtUtc { get; set; }
}

internal sealed class SettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>自定义类别（S14-C/CHG-012：categories 表；内置七类不落库）。</summary>
internal sealed class CategoryEntity
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public int Sort { get; set; }
}

/// <summary>日程条目（S18/CHG-015：schedule_items 表；时间列 Unix 秒，D-23）。
/// message_id 为软引用（不设 FK）：来源邮件仅用于追溯，邮件行不受日程生命周期约束。</summary>
internal sealed class ScheduleItemEntity
{
    public string Id { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string? MessageId { get; set; }
    public string Source { get; set; } = "canvas";
    public string Title { get; set; } = string.Empty;
    public string? Course { get; set; }
    public string? CourseCode { get; set; }
    public string? Link { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
    public long DueAtUtc { get; set; }
    public int Status { get; set; }
    public long? RemindedDueAt { get; set; }
    public long CreatedAtUtc { get; set; }
    public long UpdatedAtUtc { get; set; }
}
