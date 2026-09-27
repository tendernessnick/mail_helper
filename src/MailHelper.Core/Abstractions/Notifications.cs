namespace MailHelper.Core.Abstractions;

/// <summary>一条待发送的 Toast 通知（04 §4：ToastContentBuilder 实现；点击 launch 直达邮件，FR-14 AC1）。</summary>
public sealed record ToastNotification(string Title, string Body, string? LaunchArgument = null);

/// <summary>Toast 发送抽象（Infrastructure 以 Microsoft.Toolkit.Uwp.Notifications 实现；测试以内存假件替换）。</summary>
public interface IToastSender
{
    Task SendAsync(ToastNotification notification, CancellationToken ct);
}

/// <summary>通知日志记录（notification_log 行；level ∈ P0/P1=已发、queued=勿扰待补发、flushed=已随摘要补发，D-51）。</summary>
public sealed record NotificationLogEntry(string MessageId, string Level, DateTimeOffset LoggedAtUtc);

/// <summary>通知日志仓储（04 §8：message_id 去重 FR-14 AC2 + 勿扰 queued 补发）。</summary>
public interface INotificationStore
{
    /// <summary>过滤从未通知过的 message_id（NOT EXISTS(notification_log)，04 §8.4）。</summary>
    Task<IReadOnlyList<string>> FilterUnnotifiedAsync(IReadOnlyList<string> messageIds, CancellationToken ct);

    /// <summary>批量登记（重放幂等：已存在行不重复插入）。</summary>
    Task LogRangeAsync(IReadOnlyList<NotificationLogEntry> entries, CancellationToken ct);

    /// <summary>待补发计数（level=queued）。</summary>
    Task<int> CountQueuedAsync(CancellationToken ct);

    /// <summary>勿扰结束：全部 queued → flushed，返回补发条数（摘要基数）。</summary>
    Task<int> FlushQueuedAsync(CancellationToken ct);
}
