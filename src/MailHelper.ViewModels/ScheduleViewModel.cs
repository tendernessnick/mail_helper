using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.ViewModels;

/// <summary>日程行（S18/CHG-015）：作业标题 + 课程 + 截止时间/剩余 + 状态徽章 + 操作（完成/忽略/恢复）。</summary>
public partial class ScheduleItemRowViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string CourseDisplay { get; init; } = string.Empty;

    public string? Link { get; init; }

    public required DateTime DueAtUtc { get; init; }

    public required ScheduleItemStatus Status { get; init; }

    /// <summary>截止显示（本机时区，"MM-dd HH:mm"）。</summary>
    public string DueText => DueAtUtc.ToLocalTime().ToString("MM-dd HH:mm");

    /// <summary>相对剩余（已逾期 N 天 / 今天 HH:mm 截止 / 明天截止 / 还剩 N 天…）。</summary>
    public string RelativeText
    {
        get
        {
            var local = DueAtUtc.ToLocalTime();
            var span = DueAtUtc - DateTime.UtcNow;
            if (span.TotalSeconds < 0)
            {
                return $"已逾期 {FormatSpan(-span)}";
            }

            var today = DateTime.Now.Date;
            return local.Date == today ? $"今天 {local:HH:mm} 截止"
                : local.Date == today.AddDays(1) ? "明天截止"
                : $"还剩 {FormatSpan(span)}";
        }
    }

    public bool IsOverdue => DueAtUtc < DateTime.UtcNow && Status == ScheduleItemStatus.Open;

    public bool IsOpen => Status == ScheduleItemStatus.Open;

    public bool HasLink => !string.IsNullOrEmpty(Link);

    public string StatusText => Status switch
    {
        ScheduleItemStatus.Done => "已完成",
        ScheduleItemStatus.Ignored => "已忽略",
        _ => "待完成",
    };

    private static string FormatSpan(TimeSpan span) => span.TotalDays >= 2
        ? $"{span.TotalDays:F0} 天"
        : span.TotalHours >= 1 ? $"{span.TotalHours:F0} 小时"
        : $"{Math.Max(1, span.TotalMinutes):F0} 分钟";

    public static ScheduleItemRowViewModel From(ScheduleItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        CourseDisplay = string.IsNullOrWhiteSpace(item.Course)
            ? item.CourseCode ?? "课程未知"
            : item.Course,
        Link = item.Link,
        DueAtUtc = item.DueAtUtc,
        Status = item.Status,
    };
}

/// <summary>日程分组（已逾期 / 今天 / 未来 7 天 / 更远 / 已完成·已忽略；空组不生成）。</summary>
public record ScheduleGroupViewModel(string Header, string HeaderBadge, IReadOnlyList<ScheduleItemRowViewModel> Rows);

/// <summary>日程页视图模型（S18/CHG-015）：SyncRoundCompleted 联动自动刷新；完成/忽略/恢复即时落库；
/// 分组随本机时间计算（ADR-006 时区语义）。</summary>
public partial class ScheduleViewModel : ObservableObject
{
    private readonly ScheduleService _schedule;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILogger<ScheduleViewModel> _logger;
    private readonly SyncCoordinator _sync;

    public ScheduleViewModel(
        ScheduleService schedule,
        SyncCoordinator sync,
        IMainThreadDispatcher dispatcher,
        ILogger<ScheduleViewModel>? logger = null)
    {
        _schedule = schedule;
        _sync = sync;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<ScheduleViewModel>.Instance;
        _sync.SyncRoundCompleted += async (_, _) =>
        {
            try
            {
                await RefreshAsync(CancellationToken.None); // 每轮同步后联动（提取在 App 接线层执行）
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ui.schedule_refresh_failed");
            }
        };
    }

    public ObservableCollection<ScheduleGroupViewModel> Groups { get; } = new();

    /// <summary>未完成条目数（页头汇总）。</summary>
    [ObservableProperty]
    private int openCount;

    /// <summary>最近一轮提取的摘要文案（空=尚未运行）。</summary>
    [ObservableProperty]
    private string lastSyncText = string.Empty;

    [ObservableProperty]
    private bool hasItems;

    /// <summary>加载/刷新（页面进入与同步轮完成时调用）。</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var items = await _schedule.GetUpcomingAsync(ct);
        await _dispatcher.InvokeOnMainThreadAsync(() => RebuildGroups(items));
    }

    [RelayCommand]
    private async Task RefreshCommandAsync(CancellationToken ct) => await RefreshAsync(ct);

    [RelayCommand]
    private async Task CompleteAsync(ScheduleItemRowViewModel? row)
    {
        if (row is not null)
        {
            await ChangeStatusAsync(row, ScheduleItemStatus.Done);
        }
    }

    [RelayCommand]
    private async Task IgnoreAsync(ScheduleItemRowViewModel? row)
    {
        if (row is not null)
        {
            await ChangeStatusAsync(row, ScheduleItemStatus.Ignored);
        }
    }

    [RelayCommand]
    private async Task RestoreAsync(ScheduleItemRowViewModel? row)
    {
        if (row is not null)
        {
            await ChangeStatusAsync(row, ScheduleItemStatus.Open);
        }
    }

    private async Task ChangeStatusAsync(ScheduleItemRowViewModel row, ScheduleItemStatus status)
    {
        try
        {
            await _schedule.SetStatusAsync(row.Id, status, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ui.schedule_status_failed item_id_set=true");
        }
    }

    /// <summary>分组重建（UI 线程；记录提取摘要供页头展示）。</summary>
    internal async Task HandleRoundCompletedAsync(ScheduleExtractionSummary summary)
    {
        LastSyncText = summary.Scanned > 0 || summary.Found > 0
            ? $"本轮识别 {summary.Scanned} 封 Canvas 通知，新增 {summary.Inserted}、更新 {summary.UpdatedDue} 项"
            : string.Empty;
        await RefreshAsync(CancellationToken.None);
    }

    private void RebuildGroups(IReadOnlyList<ScheduleItem> items)
    {
        var nowLocal = DateTime.Now;
        var today = nowLocal.Date;
        List<ScheduleItemRowViewModel> overdue = [];
        List<ScheduleItemRowViewModel> todayList = [];
        List<ScheduleItemRowViewModel> week = [];
        List<ScheduleItemRowViewModel> later = [];
        List<ScheduleItemRowViewModel> finished = [];

        foreach (var item in items)
        {
            var row = ScheduleItemRowViewModel.From(item);
            if (item.Status != ScheduleItemStatus.Open)
            {
                finished.Add(row);
            }
            else if (item.DueAtUtc.ToLocalTime() < today)
            {
                overdue.Add(row);
            }
            else if (item.DueAtUtc.ToLocalTime() < today.AddDays(1))
            {
                todayList.Add(row);
            }
            else if (item.DueAtUtc.ToLocalTime() < today.AddDays(7))
            {
                week.Add(row);
            }
            else
            {
                later.Add(row);
            }
        }

        Groups.Clear();
        AddGroup("已逾期", "尽快处理或改期", overdue);
        AddGroup("今天", "当日截止", todayList);
        AddGroup("未来 7 天", "本周到期", week);
        AddGroup("更远", "已排期", later);
        AddGroup("已完成 / 已忽略", "不参与提醒", finished);
        HasItems = items.Count > 0;
        OpenCount = overdue.Count + todayList.Count + week.Count + later.Count;
    }

    private void AddGroup(string header, string badge, List<ScheduleItemRowViewModel> rows)
    {
        if (rows.Count > 0)
        {
            Groups.Add(new ScheduleGroupViewModel(header, badge, rows));
        }
    }
}
