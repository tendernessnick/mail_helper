using System.Text.RegularExpressions;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Schedule;
using MailHelper.Core.TextProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.Core.Services;

/// <summary>一轮提取结果汇总（日志 schedule.extracted 的结构化来源；不含主题/标题，红线）。</summary>
public sealed record ScheduleExtractionSummary(
    int Scanned, int Found, int Inserted, int UpdatedDue, int ParseFailures, int RemindersSent);

/// <summary>日程服务（S18/CHG-015，ADR-006）：SyncRoundCompleted 驱动——
/// ①ExtractPendingAsync：取 Canvas 域未扫描邮件 → 正文（BodyCache HTML→Normalize，缺省回退预览）→
/// CanvasDdlExtractor 解析 → 按去重键 upsert（due 变化清提醒标记）→ 扫过即置位（首次启用自动回填历史）；
/// ②CheckRemindersAsync：open 且 due ∈ [now, now+提前量] 且未按当前 due 提醒过 → Toast（同一 due 恰一次）。
/// 日志仅计数（红线同 NotificationService）。</summary>
public sealed class ScheduleService
{
    /// <summary>终态条目保留期（Done/Ignored 超 90 天清理，防无限增长）。</summary>
    public const int FinishedRetentionDays = 90;

    /// <summary>单轮扫描批大小（与分类批同量级；周报摘要单封即可含全部条目）。</summary>
    public const int DefaultBatchSize = 50;

    /// <summary>锚点 href 展开：Canvas 邮件的链接只存在于 href 属性（文本仅 "Click to view"），
    /// 不展开则链接丢失 → 去重键退化为标题哈希，due 变更会误建新条（D-72，实测缺陷）。</summary>
    private static readonly Regex AnchorHrefRegex = new(
        @"<a\b[^>]*href\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly IMessageStore _messages;
    private readonly IScheduleStore _schedule;
    private readonly IBodyCache _bodyCache;
    private readonly ISettingsStore _settings;
    private readonly IToastSender _toasts;
    private readonly string _accountId;
    private readonly ILogger<ScheduleService> _logger;
    private readonly TimeProvider _clock;
    private readonly CanvasDdlExtractor _extractor = new();

    public ScheduleService(
        IMessageStore messages,
        IScheduleStore schedule,
        IBodyCache bodyCache,
        ISettingsStore settings,
        IToastSender toasts,
        string accountId,
        ILogger<ScheduleService>? logger = null,
        TimeProvider? clock = null)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _bodyCache = bodyCache ?? throw new ArgumentNullException(nameof(bodyCache));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _logger = logger ?? NullLogger<ScheduleService>.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>提取管线（AC1/AC2/AC5）：候选批 → 逐封解析 → upsert → 扫描置位 → 终态清理。
    /// schedule.enabled 关闭时直接返回空汇总（不扫不置位，重新打开后下一轮继续）。</summary>
    public async Task<ScheduleExtractionSummary> ExtractPendingAsync(
        int batchSize = DefaultBatchSize, CancellationToken ct = default)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        if (!await IsScheduleEnabledAsync(ct))
        {
            return new ScheduleExtractionSummary(0, 0, 0, 0, 0, 0);
        }

        var candidates = await _messages.GetDdlCandidatesAsync(_accountId, batchSize, ct);
        var scanned = 0;
        var found = 0;
        var inserted = 0;
        var updatedDue = 0;
        var parseFailures = 0;
        var scannedIds = new List<string>(candidates.Count);

        foreach (var mail in candidates)
        {
            scannedIds.Add(mail.Id);
            scanned++;
            try
            {
                var text = await ReadBodyTextAsync(mail, ct);
                var parsed = _extractor.Parse(text, nowUtc);
                found += parsed.Count;
                foreach (var candidate in parsed)
                {
                    var item = new ScheduleItem(
                        Guid.NewGuid().ToString("N"), _accountId, mail.Id, CanvasDdlExtractor.SourceCanvas,
                        candidate.Title, candidate.Course, candidate.CourseCode, candidate.Link,
                        candidate.DedupeKey, ToUtc(candidate.DueLocal), ScheduleItemStatus.Open, null, nowUtc, nowUtc);
                    var (_, wasInserted, dueUpdated) = await _schedule.UpsertByDedupeKeyAsync(item, ct);
                    if (wasInserted)
                    {
                        inserted++;
                    }
                    else if (dueUpdated)
                    {
                        updatedDue++;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                parseFailures++; // SCHED-001：单封失败不阻断批次（该封仍置位避免死循环重扫，AC5）
                _logger.LogWarning(ex, "schedule.extract_failed err_code=SCHED-001");
            }
        }

        if (scannedIds.Count > 0)
        {
            await _messages.MarkDdlScannedAsync(scannedIds, nowUtc, ct);
        }

        var cleaned = await _schedule.DeleteFinishedBeforeAsync(
            nowUtc.AddDays(-FinishedRetentionDays), ct);

        _logger.LogInformation(
            "schedule.extracted scanned={Scanned} found={Found} inserted={Inserted} updated_due={UpdatedDue} failed={Failed} cleaned={Cleaned}",
            scanned, found, inserted, updatedDue, parseFailures, cleaned);
        return new ScheduleExtractionSummary(scanned, found, inserted, updatedDue, parseFailures, 0);
    }

    /// <summary>临期提醒（AC7）：open 且 due ∈ [now, now+提前量] 且 RemindedDueAt ≠ 当前 due → Toast 一次。
    /// 返回发送条数；总开关/提醒开关任一关闭即不发。</summary>
    public async Task<int> CheckRemindersAsync(CancellationToken ct = default)
    {
        if (!await IsScheduleEnabledAsync(ct)
            || await _settings.GetAsync(SettingsService.KeyScheduleReminderEnabled, ct) is "false")
        {
            return 0;
        }

        var hours = await GetReminderHoursAsync(ct);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var windowEnd = nowUtc.AddHours(hours);
        var items = await _schedule.GetAllAsync(_accountId, ct);
        var sent = 0;
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (item.Status != ScheduleItemStatus.Open
                || item.DueAtUtc < nowUtc || item.DueAtUtc > windowEnd)
            {
                continue;
            }

            var dueUnix = new DateTimeOffset(item.DueAtUtc.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();
            if (item.RemindedDueAt == dueUnix)
            {
                continue; // 同一 due 只提醒一次（due 变更后 RemindedDueAt 被清空 → 重新具备资格）
            }

            await _toasts.SendAsync(new ToastNotification(
                "作业截止提醒",
                $"{item.Title}（{item.Course ?? "课程未知"}）· {FormatRemaining(item.DueAtUtc, nowUtc)}"), ct);
            await _schedule.MarkRemindedAsync(item.Id, dueUnix, ct);
            sent++;
        }

        if (sent > 0)
        {
            _logger.LogInformation("schedule.reminded sent={Sent} window_hours={Hours}", sent, hours);
        }

        return sent;
    }

    /// <summary>全部条目（仓储已按 open 升序 → 终态垫底排序，日程页直接渲染）。</summary>
    public Task<IReadOnlyList<ScheduleItem>> GetUpcomingAsync(CancellationToken ct = default) =>
        _schedule.GetAllAsync(_accountId, ct);

    public Task<int> CountOpenAsync(CancellationToken ct = default) =>
        _schedule.CountOpenAsync(_accountId, ct);

    /// <summary>用户操作：标记完成/忽略/恢复（US-18-2）。</summary>
    public Task SetStatusAsync(string itemId, ScheduleItemStatus status, CancellationToken ct = default) =>
        _schedule.SetStatusAsync(itemId, status, ct);

    /// <summary>提前提醒小时数（设置键钳 1–168；独立暴露便于 VM 与测试）。</summary>
    public async Task<int> GetReminderHoursAsync(CancellationToken ct = default) =>
        int.TryParse(await _settings.GetAsync(SettingsService.KeyScheduleReminderHours, ct), out var hours)
            ? Math.Clamp(hours, 1, 168)
            : 24;

    private async Task<bool> IsScheduleEnabledAsync(CancellationToken ct) =>
        await _settings.GetAsync(SettingsService.KeyScheduleEnabled, ct) is not "false";

    /// <summary>正文文本：锚点 href 展开为文本 → BodyCache HTML → Normalize；缓存缺失回退 BodyPreview
    /// （500 字符，单封通知通常足够）。</summary>
    private async Task<string?> ReadBodyTextAsync(MailMessage mail, CancellationToken ct)
    {
        if (mail.BodyPath is { Length: > 0 } relative)
        {
            try
            {
                var html = await _bodyCache.ReadAsync(relative, ct);
                if (html is not null)
                {
                    return TextNormalizer.Normalize(ExpandAnchorHrefs(html));
                }
            }
            catch (IOException)
            {
                // 缓存读失败回落预览（与阅读窗格同语义）
            }
        }

        return mail.BodyPreview;
    }

    /// <summary>"&lt;a href=U&gt;T&lt;/a&gt;" → "T (U)"：链接进入文本层供 CanvasDdlExtractor 提取。</summary>
    private static string ExpandAnchorHrefs(string html) =>
        AnchorHrefRegex.Replace(html, m => $"{m.Groups["text"].Value} ({m.Groups["href"].Value})");

    /// <summary>Canvas 墙上时间 → UTC（本机时区解释，ADR-006）。</summary>
    private static DateTime ToUtc(DateTime dueLocal) => TimeZoneInfo.ConvertTimeToUtc(dueLocal);

    /// <summary>剩余时间文案（提醒 Toast 用）。</summary>
    private static string FormatRemaining(DateTime dueUtc, DateTime nowUtc)
    {
        var span = dueUtc - nowUtc;
        return span.TotalHours >= 48
            ? $"还剩 {span.TotalDays:F0} 天"
            : span.TotalHours >= 1 ? $"还剩 {span.TotalHours:F0} 小时"
            : $"还剩 {Math.Max(1, span.TotalMinutes):F0} 分钟";
    }
}
