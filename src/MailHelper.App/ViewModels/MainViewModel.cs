using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace MailHelper.App.ViewModels;

/// <summary>邮件列表行（05 §3.2 中栏行结构：重要度色条/徽章、发件人、主题、摘要、时间、附件图标）。</summary>
public partial class MailItemViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Subject { get; init; }

    public required string FromDisplay { get; init; }

    public required string Preview { get; init; }

    public required string ReceivedText { get; init; }

    public required MailCategory Category { get; init; }

    public required Importance Importance { get; init; }

    public required double Confidence { get; init; }

    public required string? HtmlPath { get; init; }

    public required bool HasAttachments { get; init; }

    [ObservableProperty]
    private bool isRead;

    public string ImportanceText => Importance.ToString();

    partial void OnIsReadChanged(bool value) => OnPropertyChanged(nameof(UnreadGlyph));

    public string UnreadGlyph => IsRead ? string.Empty : "●";

    public static MailItemViewModel From(MailMessage message, DateTime nowUtc) => new()
    {
        Id = message.Id,
        Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(无主题)" : message.Subject,
        FromDisplay = string.IsNullOrWhiteSpace(message.FromName) ? message.FromAddress ?? "?" : message.FromName,
        Preview = message.BodyPreview ?? string.Empty,
        ReceivedText = FormatReceived(message.ReceivedAtUtc, nowUtc),
        Category = message.Category,
        Importance = message.Importance,
        Confidence = message.Confidence ?? 0,
        HtmlPath = message.BodyPath,
        HasAttachments = message.HasAttachments,
        IsRead = message.IsRead,
    };

    private static string FormatReceived(DateTime receivedUtc, DateTime nowUtc)
    {
        var local = receivedUtc.ToLocalTime();
        var today = nowUtc.ToLocalTime().Date;
        return local.Date == today ? local.ToString("HH:mm")
            : local.Date == today.AddDays(-1) ? "昨天"
            : local.Date == today.AddDays(-7) ? "本周"
            : local.ToString("MM-dd");
    }
}

/// <summary>左栏导航项（05 §3.2：全部/待确认/7 类别 + 未读计数；「待确认」为 FR-09 队列入口）。</summary>
public partial class CategoryItemViewModel : ObservableObject
{
    public required MailCategory? Category { get; init; }

    public required string Label { get; init; }

    public required string Icon { get; init; }

    public required bool IsNeedsReviewEntry { get; init; }

    [ObservableProperty]
    private int count;
}

/// <summary>主视图模型：Onboarding ↔ 三栏收件箱（05 §2 信息架构；D-44：单窗双态）。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly SyncCoordinator _sync;
    private readonly ClassificationService _classifier;
    private readonly FeedbackService _feedback;
    private readonly SearchService _search;
    private readonly IMessageStore _store;
    private readonly ISettingsStore _settings;
    private readonly IBodyCache _bodyCache;
    private readonly IAccountStore _accounts;
    private readonly IMailProvider _provider;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly bool _devMode;

    private string? _accountId;

    public MainViewModel(
        AuthService auth,
        SyncCoordinator sync,
        ClassificationService classifier,
        FeedbackService feedback,
        SearchService search,
        IMessageStore store,
        ISettingsStore settings,
        IBodyCache bodyCache,
        IAccountStore accounts,
        IMailProvider provider,
        ILogger<MainViewModel> logger,
        bool devMode)
    {
        _auth = auth;
        _sync = sync;
        _classifier = classifier;
        _feedback = feedback;
        _search = search;
        _store = store;
        _settings = settings;
        _bodyCache = bodyCache;
        _accounts = accounts;
        _provider = provider;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _devMode = devMode;

        Categories.Add(new CategoryItemViewModel { Category = null, Label = "收件箱（全部）", Icon = "📥", IsNeedsReviewEntry = false });
        Categories.Add(new CategoryItemViewModel { Category = null, Label = "待确认", Icon = "⏰", IsNeedsReviewEntry = true });
        foreach (var (category, label, icon) in CategoryLabels)
        {
            Categories.Add(new CategoryItemViewModel { Category = category, Label = label, Icon = icon, IsNeedsReviewEntry = false });
        }

        _sync.StateChanged += (_, e) => OnSyncStateChanged(e);
    }

    public ObservableCollection<CategoryItemViewModel> Categories { get; } = new();

    public ObservableCollection<MailItemViewModel> Mails { get; } = new();

    [ObservableProperty]
    private CategoryItemViewModel? selectedCategory;

    [ObservableProperty]
    private MailItemViewModel? selectedMail;

    [ObservableProperty]
    private bool isOnboarding = true;

    [ObservableProperty]
    private bool privacyAccepted;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string syncStatusText = "尚未同步";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool unreadOnly;

    [ObservableProperty]
    private bool isSearchMode;

    /// <summary>全部未读数（托盘角标数据源，FR-14 角标计数）。</summary>
    [ObservableProperty]
    private int unreadTotal;

    // —— 视图导航（05 §2 Shell：收件箱/规则/设置）——
    [ObservableProperty]
    private bool isInboxView = true;

    [ObservableProperty]
    private bool isRulesView;

    [ObservableProperty]
    private bool isSettingsView;

    [RelayCommand]
    private void ShowInbox() => SwitchView(0);

    [RelayCommand]
    private void ShowRules() => SwitchView(1);

    [RelayCommand]
    private void ShowSettings() => SwitchView(2);

    private void SwitchView(int view)
    {
        IsInboxView = view == 0;
        IsRulesView = view == 1;
        IsSettingsView = view == 2;
    }

    public event EventHandler<MailItemViewModel?>? SelectedMailHtmlNeeded;

    partial void OnSelectedMailChanged(MailItemViewModel? value)
    {
        if (value is not null && !value.IsRead)
        {
            value.IsRead = true;
            _ = MarkReadAsync(value.Id, value.Category);
        }

        SelectedMailHtmlNeeded?.Invoke(this, value);
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        var account = (await _accounts.FindAllAsync(ct)).FirstOrDefault();
        if (account is null)
        {
            IsOnboarding = true;
            return;
        }

        _accountId = account.Id;
        IsOnboarding = false;
        await LoadInboxAsync(ct);
        _ = RunInitialSyncAsync(ct);
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            if (_provider.Kind == ChannelKind.OutlookDesktop)
            {
                // CHG-011：桌面通道复用本机 Outlook 登录态，无 OAuth 登录环节，直接探测 COM 可达性
                string? address;
                try
                {
                    address = await _provider.GetAccountAddressAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ui.outlook_connect_failed");
                    SyncStatusText = "连接失败：读不到本机经典版 Outlook（请确认它已打开并登录学校邮箱后重试）。";
                    return;
                }

                if (string.IsNullOrWhiteSpace(address))
                {
                    SyncStatusText = "连接失败：Outlook 未返回登录账户（请确认已配置学校邮箱账户）。";
                    return;
                }

                _accountId = "acc-1";
                await _accounts.UpsertAccountAsync(new Account(
                    _accountId, address, null, null, ChannelKind.OutlookDesktop, null,
                    AccountStatus.Active, DateTime.UtcNow), ct);
                var domain = address.Contains('@') ? address[(address.LastIndexOf('@') + 1)..] : "unknown";
                _logger.LogInformation("ui.outlook_connected account_domain={Domain}", domain); // 隐私：只记域名
                IsOnboarding = false;
                await RunInitialSyncAsync(ct);
                return;
            }

            var result = await _auth.SignInAsync(ct);
            if (!result.IsSuccess)
            {
                SyncStatusText = $"登录失败：{result.ErrorCode} {result.Message}";
                return;
            }

            var email = result.Token?.Email ?? "dev@localhost";
            _accountId = "acc-1";
            await _accounts.UpsertAccountAsync(new Account(
                _accountId, email, null, result.Token?.TenantId, ChannelKind.Graph, null,
                AccountStatus.Active, DateTime.UtcNow), ct);
            IsOnboarding = false;
            await RunInitialSyncAsync(ct);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect() => PrivacyAccepted && !IsBusy;

    partial void OnPrivacyAcceptedChanged(bool value) => ConnectCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task SyncNowAsync(CancellationToken ct)
    {
        if (_accountId is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _sync.SyncNowAsync(ct: ct);
            var summary = await _classifier.ClassifyPendingAsync(200, ct); // 同步完成后走分类管线（03 §5.2）
            await LoadInboxAsync(ct);
            _logger.LogInformation(
                "ui.sync_cycle_done classified={Classified} pending_review={PendingReview}", summary.Processed, summary.PendingReview);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedCategoryChanged(CategoryItemViewModel? value)
    {
        IsSearchMode = false;
        _ = LoadInboxAsync(CancellationToken.None); // 左栏点击即过滤（05 §3.2）
    }

    [RelayCommand]
    private async Task SearchAsync(CancellationToken ct)
    {
        if (_accountId is null || string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        var results = await _search.SearchAsync(SearchText.Trim(), 200, ct); // FR-13 语法搜索（S10）
        await _dispatcher.InvokeAsync(() =>
        {
            ReplaceMails(results);
            IsSearchMode = true;
            SyncStatusText = $"搜索「{SearchText.Trim()}」：{results.Count} 条";
        });
    }

    [RelayCommand]
    private async Task ClearSearchAsync(CancellationToken ct)
    {
        SearchText = string.Empty;
        IsSearchMode = false;
        await LoadInboxAsync(ct);
    }

    [RelayCommand]
    private async Task ToggleUnreadOnlyAsync(CancellationToken ct) => await LoadInboxAsync(ct);

    public async Task RunInitialSyncAsync(CancellationToken ct)
    {
        await SyncNowAsync(ct);
        if (_devMode)
        {
            await LoadInboxAsync(ct);
        }
    }

    private async Task MarkReadAsync(string messageId, MailCategory category)
    {
        await _store.MarkReadAsync(messageId, CancellationToken.None);
        await _dispatcher.InvokeAsync(() =>
        {
            // 左栏未读数联动（「全部」+ 对应类别；待确认入口计数与此无关）
            foreach (var item in Categories.Where(c =>
                         (c.Category is null && !c.IsNeedsReviewEntry) || c.Category == category))
            {
                if (item.Count > 0)
                {
                    item.Count--;
                }
            }
        });
    }

    private async Task LoadInboxAsync(CancellationToken ct)
    {
        if (_accountId is null)
        {
            return;
        }

        var query = new InboxQuery(
            Category: SelectedCategory?.Category,
            NeedsReviewOnly: SelectedCategory?.IsNeedsReviewEntry == true,
            UnreadOnly: UnreadOnly);
        var messages = await _store.GetInboxAsync(_accountId, query, ct);
        var nowUtc = DateTime.UtcNow;
        var unread = await _store.GetUnreadCountsAsync(_accountId, ct);
        var reviewCount = await _store.GetNeedsReviewCountAsync(_accountId, ClassificationService.DefaultReviewThreshold, ct);

        await _dispatcher.InvokeAsync(() =>
        {
            ReplaceMails(messages);
            UnreadTotal = unread.Values.Sum(); // 托盘角标（FR-14）
            foreach (var item in Categories)
            {
                item.Count = item.IsNeedsReviewEntry
                    ? reviewCount
                    : item.Category is { } c ? unread[c]
                    : messages.Count(m => !m.IsRead);
            }
        });
    }

    /// <summary>Toast 点击直达（FR-14 AC1，launch=mailhelper://message/{id}）：切回全部收件箱并选中该邮件。</summary>
    public async Task SelectMailByIdAsync(string messageId, CancellationToken ct)
    {
        if (_accountId is null)
        {
            return;
        }

        var allCategory = Categories.First(c => c.Category is null && !c.IsNeedsReviewEntry);
        if (SelectedCategory != allCategory)
        {
            SelectedCategory = allCategory; // 触发 LoadInboxAsync
            await Task.Delay(50, ct); // 等列表刷新（LoadInboxAsync 内部 invoke 队列）
        }
        else
        {
            await LoadInboxAsync(ct);
        }

        await _dispatcher.InvokeAsync(() =>
        {
            SelectedMail = Mails.FirstOrDefault(m => m.Id == messageId) ?? SelectedMail;
        });
    }

    private void ReplaceMails(IReadOnlyList<MailMessage> messages)
    {
        Mails.Clear();
        foreach (var message in messages)
        {
            Mails.Add(MailItemViewModel.From(message, DateTime.UtcNow));
        }
    }

    private void OnSyncStateChanged(SyncStateChangedEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            SyncStatusText = e.NewState switch
            {
                SyncState.Syncing => "正在同步…",
                SyncState.Offline => $"离线 · {e.ErrorCode}",
                SyncState.Error => $"同步失败 · {e.ErrorCode}（悬停查看重试）",
                SyncState.ReauthRequired => "需要重新登录",
                _ => "就绪",
            };
        });
    }

    /// <summary>改判（FR-11/TC-014 入口）：立即生效并由反馈闭环自动生成发件人规则。</summary>
    public async Task ApplyCorrectionAsync(MailCategory newCategory, CancellationToken ct)
    {
        if (SelectedMail is not { } mail)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (await _feedback.ApplyCorrectionAsync(mail.Id, newCategory, null, ct))
            {
                SyncStatusText = $"已改判为「{LabelOf(newCategory)}」；同发件人后续邮件将自动归入该类别";
                await LoadInboxAsync(ct); // 徽章/当前视图联动（如「待确认」队列移除该邮件）
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string LabelOf(MailCategory category) =>
        Array.Find(CategoryLabels, pair => pair.Category == category).Label;

    /// <summary>阅读窗格内容：优先正文缓存 HTML，缺失时回退纯文本预览（04 §4.1 按需拉正文本期不做）。</summary>
    public async Task<string?> BuildReaderHtmlAsync(MailItemViewModel mail, CancellationToken ct)
    {
        if (mail.HtmlPath is { Length: > 0 } relative)
        {
            var html = await _bodyCache.ReadAsync(relative, ct);
            if (html is not null)
            {
                return html;
            }
        }

        var text = System.Net.WebUtility.HtmlEncode(mail.Preview);
        return $"<html><body style=\"font-family:Segoe UI,'Microsoft YaHei';font-size:14px;color:#201F1E;\"><pre style=\"white-space:pre-wrap;font-family:inherit\">{text}</pre></body></html>";
    }

    public static readonly (MailCategory Category, string Label, string Icon)[] CategoryLabels =
    {
        (MailCategory.Course, "课程学习", "📚"),
        (MailCategory.Career, "职业发展", "💼"),
        (MailCategory.Admin, "校园事务", "🏫"),
        (MailCategory.Finance, "财务缴费", "💰"),
        (MailCategory.Announce, "通知公告", "📢"),
        (MailCategory.Subscription, "订阅营销", "📨"),
        (MailCategory.Other, "其他", "🗂"),
    };
}
