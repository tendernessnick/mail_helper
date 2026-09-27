using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.Core.Services;

/// <summary>一轮通知处理结果（埋点 notify.sent 字段来源；SentToasts=实际弹出的 Toast 数）。</summary>
public sealed record NotificationOutcome(
    int SentToasts, int P0Count, int P1Count, int Aggregated, int Queued, int Flushed);

/// <summary>通知决策（04 §8 MOD-07）：过滤 Importance ≥ P1 → message_id 去重（FR-14 AC2）→
/// P0 逐封 Toast、P1 ≥3 封聚合摘要、否则逐封；勿扰时段命中写 queued、结束后补发摘要；首轮静默仅登记（D-50）。
/// 不落任何主题/发件人日志（红线：notify.sent 仅含计数）。</summary>
public sealed class NotificationService
{
    public const string P0EnabledKey = "notify.p0_enabled";
    public const string P1EnabledKey = "notify.p1_enabled";
    public const string QuietHoursKey = "notify.quiet_hours";
    public const int P1AggregateThreshold = 3;
    public const string LaunchPrefix = "mailhelper://message/";

    private readonly INotificationStore _store;
    private readonly IToastSender _sender;
    private readonly ISettingsStore _settings;
    private readonly ILogger<NotificationService> _logger;
    private readonly TimeProvider _clock;

    public NotificationService(
        INotificationStore store,
        IToastSender sender,
        ISettingsStore settings,
        ILogger<NotificationService>? logger = null,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullLogger<NotificationService>.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>处理一轮新邮件（由 SyncCoordinator.SyncRoundCompleted 驱动；亦可独立调用）。</summary>
    public async Task<NotificationOutcome> HandleNewMailsAsync(
        IReadOnlyList<MailMessage> newMails, bool initialRound = false, CancellationToken ct = default)
    {
        var p0Enabled = await GetFlagAsync(P0EnabledKey, ct);
        var p1Enabled = await GetFlagAsync(P1EnabledKey, ct);
        var quiet = await ParseQuietHoursAsync(ct);

        var candidates = newMails
            .Where(m => !m.IsDeletedRemote)
            .Where(m => m.Importance == Importance.P0 && p0Enabled || m.Importance == Importance.P1 && p1Enabled)
            .ToList();

        var fresh = new List<MailMessage>();
        if (candidates.Count > 0)
        {
            var unnotified = await _store.FilterUnnotifiedAsync(candidates.Select(m => m.Id).ToList(), ct);
            var freshIds = unnotified.ToHashSet();
            fresh = candidates.Where(m => freshIds.Contains(m.Id)).ToList();
        }

        var inQuietHours = quiet is { } window && IsInQuietHours(TimeOnly.FromTimeSpan(_clock.GetLocalNow().TimeOfDay), window);

        if (initialRound)
        {
            await LogAsync(fresh, queued: false, ct); // 首轮静默：仅登记去重（D-50）
            return new NotificationOutcome(0, 0, fresh.Count(m => m.Importance == Importance.P1), 0, 0, 0);
        }

        var flushed = 0;
        var sent = 0;
        if (!inQuietHours)
        {
            flushed = await _store.FlushQueuedAsync(ct);
            if (flushed > 0)
            {
                await _sender.SendAsync(new ToastNotification(
                    "MailHelper 重要邮件",
                    $"{flushed} 封重要邮件（勿扰时段积压）"), ct); // 04 §8.3 时段结束补发摘要
                sent++;
            }
        }

        var p0 = fresh.Where(m => m.Importance == Importance.P0).ToList();
        var p1 = fresh.Where(m => m.Importance == Importance.P1).ToList();

        if (inQuietHours)
        {
            await LogAsync(fresh, queued: true, ct);
            _logger.LogInformation(
                "notify.queued count={Count}", fresh.Count);
            return new NotificationOutcome(0, p0.Count, p1.Count, 0, fresh.Count, 0);
        }

        foreach (var mail in p0)
        {
            await _sender.SendAsync(new ToastNotification(
                mail.Subject ?? "重要邮件",
                mail.FromName ?? "新邮件",
                LaunchPrefix + mail.Id), ct);
            sent++;
        }

        var aggregated = 0;
        if (p1.Count >= P1AggregateThreshold)
        {
            await _sender.SendAsync(new ToastNotification(
                "MailHelper 重要邮件",
                $"{p1.Count} 封重要邮件"), ct); // P1 聚合摘要（04 §8.2）
            aggregated = 1;
            sent++;
        }
        else
        {
            foreach (var mail in p1)
            {
                await _sender.SendAsync(new ToastNotification(
                    mail.Subject ?? "新邮件",
                    mail.FromName ?? "新邮件",
                    LaunchPrefix + mail.Id), ct);
                sent++;
            }
        }

        await LogAsync(fresh, queued: false, ct);

        _logger.LogInformation(
            "notify.sent sent={Sent} p0={P0} p1={P1} aggregated={Aggregated} flushed={Flushed}",
            sent, p0.Count, p1.Count, aggregated, flushed);
        return new NotificationOutcome(sent, p0.Count, p1.Count, aggregated, 0, flushed);
    }

    private async Task<bool> GetFlagAsync(string key, CancellationToken ct) =>
        await _settings.GetAsync(key, ct) is not { } value || value != "false"; // 默认开（04 §7）

    private async Task<(TimeOnly Start, TimeOnly End)?> ParseQuietHoursAsync(CancellationToken ct)
    {
        var raw = await _settings.GetAsync(QuietHoursKey, ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null; // null/空 = 未启用（04 §7 默认 null）
        }

        var parts = raw.Split('-');
        if (parts.Length == 2
            && TimeOnly.TryParse(parts[0], out var start)
            && TimeOnly.TryParse(parts[1], out var end))
        {
            return (start, end); // 格式 "HH:mm-HH:mm"，可跨午夜（D-49）
        }

        _logger.LogWarning("notify.quiet_hours_invalid"); // 非法格式按未启用处理
        return null;
    }

    private static bool IsInQuietHours(TimeOnly now, (TimeOnly Start, TimeOnly End) window)
    {
        return window.Start <= window.End
            ? now >= window.Start && now < window.End
            : now >= window.Start || now < window.End; // 跨午夜（如 23:00-07:00）
    }

    private async Task LogAsync(IReadOnlyList<MailMessage> mails, bool queued, CancellationToken ct)
    {
        if (mails.Count == 0)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        await _store.LogRangeAsync(mails
            .Select(m => new NotificationLogEntry(m.Id, queued ? "queued" : m.Importance.ToString(), now))
            .ToList(), ct);
    }
}
