using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.Core.Services;

/// <summary>两套 UI 共用的同步收尾：先分类全部待处理邮件，再通知与提取日程。
/// 由协调器等待完成后推进断点和刷新界面，周期同步与手动同步走同一条管线。</summary>
public sealed class SyncCompletionService
{
    private readonly ClassificationService _classification;
    private readonly IMessageStore _messages;
    private readonly NotificationService _notifications;
    private readonly ScheduleService _schedule;
    private readonly ILogger<SyncCompletionService> _logger;

    public SyncCompletionService(ClassificationService classification, IMessageStore messages,
        NotificationService notifications, ScheduleService schedule,
        ILogger<SyncCompletionService>? logger = null)
    {
        _classification = classification;
        _messages = messages;
        _notifications = notifications;
        _schedule = schedule;
        _logger = logger ?? NullLogger<SyncCompletionService>.Instance;
    }

    public async Task ProcessAsync(SyncRoundCompletedEventArgs round, CancellationToken ct)
    {
        const int batchSize = 200;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var summary = await _classification.ClassifyPendingAsync(batchSize, ct);
            if (summary.Processed < batchSize)
            {
                break;
            }
        }

        // 远端映射对象仍是 other/P2；必须重读落库后的类别与重要度。
        var classified = new List<MailMessage>(round.NewMails.Count);
        foreach (var id in round.NewMails.Select(m => m.Id).Distinct(StringComparer.Ordinal))
        {
            if (await _messages.GetByIdAsync(id, ct) is { IsDeletedRemote: false } mail)
            {
                classified.Add(mail);
            }
        }

        await RunOptionalAsync("notifications", () =>
            _notifications.HandleNewMailsAsync(classified, round.IsInitialRound, ct), ct);
        await RunOptionalAsync("schedule", async () =>
        {
            // 首次接入或大批历史邮件也在本轮完成回填。
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _schedule.ExtractPendingAsync(ct: ct);
                if (result.Scanned < ScheduleService.DefaultBatchSize)
                {
                    break;
                }
            }

            await _schedule.CheckRemindersAsync(ct);
        }, ct);
    }

    private async Task RunOptionalAsync(string stage, Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 辅助功能失败不丢失邮件；只记录异常类型，不记录邮件正文或标题。
            _logger.LogWarning("sync.completion_optional_failed stage={Stage} error_type={ErrorType}",
                stage, ex.GetType().Name);
        }
    }
}
