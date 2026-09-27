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
