namespace MailHelper.Core.Domain;

/// <summary>同步断点（04 章 §3.1 SYNC_STATE 实体：deltaLink / IMAP UID 水位 / 上次同步时间与状态）。
/// 命名 SyncCheckpoint 以避免与 Core.Services.SyncState（状态机枚举）冲突（D-16）。</summary>
public sealed record SyncCheckpoint(
    string AccountId,
    string? DeltaLink,
    string? ImapUidWatermark,     // JSON {folder:{validity,uid}}
    DateTime? LastSyncAtUtc,
    string? LastSyncStatus);
