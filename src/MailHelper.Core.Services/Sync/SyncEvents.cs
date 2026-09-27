using MailHelper.Core.Domain;

namespace MailHelper.Core.Services;

/// <summary>同步状态变更（04 章 §2.2 引用未定义 → CHG-007 提案定义）。
/// ErrorCode 取 SYNC-001/002、AUTH-003 等（状态为 Offline/Error/ReauthRequired 时非空）。</summary>
public sealed class SyncStateChangedEventArgs : EventArgs
{
    public SyncStateChangedEventArgs(SyncState newState, string? errorCode = null, string? message = null)
    {
        NewState = newState;
        ErrorCode = errorCode;
        Message = message;
    }

    public SyncState NewState { get; }

    public string? ErrorCode { get; }

    public string? Message { get; }
}

/// <summary>批次同步进度（04 章 §2.2：驱动 UI 进度）。Added/Updated 由 upsert 结果得出（D-36）。</summary>
public sealed class BatchSyncedEventArgs : EventArgs
{
    public BatchSyncedEventArgs(int pageIndex, int added, int updated, int removed)
    {
        PageIndex = pageIndex;
        Added = added;
        Updated = updated;
        Removed = removed;
    }

    /// <summary>已完成的入库批次序号（从 1 起）。</summary>
    public int PageIndex { get; }

    public int Added { get; }

    public int Updated { get; }

    public int Removed { get; }
}

/// <summary>整轮同步成功完成（CHG-010：04 §8.1「同步完成事件携带 newMails」的载体——
/// NewMails=本轮全部入库邮件（新增与更新，通知侧由 notification_log 去重）；
/// IsInitialRound=同步前无断点（首轮，D-50 通知静默的依据）。</summary>
public sealed class SyncRoundCompletedEventArgs : EventArgs
{
    public SyncRoundCompletedEventArgs(IReadOnlyList<MailMessage> newMails, bool isInitialRound, long durationMs)
    {
        NewMails = newMails;
        IsInitialRound = isInitialRound;
        DurationMs = durationMs;
    }

    public IReadOnlyList<MailMessage> NewMails { get; }

    public bool IsInitialRound { get; }

    public long DurationMs { get; }
}
