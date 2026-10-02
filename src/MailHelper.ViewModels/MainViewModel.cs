using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace MailHelper.ViewModels;

/// <summary>邮件列表行（05 §3.2 中栏行结构：重要度色条/徽章、发件人、主题、摘要、时间、附件图标）。</summary>
public partial class MailItemViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Subject { get; init; }

    public required string FromDisplay { get; init; }

    public required string Preview { get; init; }

    public required string ReceivedText { get; init; }

    public required string Category { get; init; } // S14-C：类别 ID

    public required Importance Importance { get; init; }

    public required double Confidence { get; init; }

    public required string? HtmlPath { get; init; }

    public required bool HasAttachments { get; init; }

    [ObservableProperty]
    private bool isRead;

    public string ImportanceText => Importance.ToString();

    /// <summary>S13-C 头像首字母（发件人显示名首个字符；CJK 原样）。</summary>
    public string Initial => string.IsNullOrWhiteSpace(FromDisplay)
        ? "?"
        : FromDisplay.Trim()[0].ToString().ToUpperInvariant();

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
    public required string? CategoryId { get; init; } // S14-C：null=全部/待确认虚拟项

    public required string Label { get; init; }

    public required string Icon { get; init; }

    public required string ColorHex { get; init; }

    public required bool IsNeedsReviewEntry { get; init; }

    [ObservableProperty]
    private int count;
}

/// <summary>主视图模型：Onboarding ↔ 三栏收件箱（05 §2 信息架构；D-44：单窗双态）。</summary>
public partial class MainViewModel : ObservableObject
{
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
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly bool _devMode;

    private string? _accountId;

    public MainViewModel(
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
        bool devMode,
        IMainThreadDispatcher dispatcher)
    {
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
        _dispatcher = dispatcher;
        _devMode = devMode;

        RebuildCategories();

        _sync.StateChanged += (_, e) => OnSyncStateChanged(e);
    }

    public ObservableCollection<CategoryItemViewModel> Categories { get; } = new();

    /// <summary>S14-C：按类别目录重建左栏（保留当前选中）；目录变更（新建/删除类别）后调用。</summary>
    public void RebuildCategories()
    {
        var selectedId = SelectedCategory?.CategoryId;
        var needsReview = SelectedCategory?.IsNeedsReviewEntry == true;
        Categories.Clear();
        Categories.Add(new CategoryItemViewModel { CategoryId = null, Label = "收件箱（全部）", Icon = "📥", ColorHex = "#0F6CBD", IsNeedsReviewEntry = false });
        Categories.Add(new CategoryItemViewModel { CategoryId = null, Label = "待确认", Icon = "⏰", ColorHex = "#F7630C", IsNeedsReviewEntry = true });
        foreach (var definition in CategoryCatalog.All)
        {
            Categories.Add(new CategoryItemViewModel
            {
                CategoryId = definition.Id, Label = definition.Label, Icon = definition.Icon,
                ColorHex = definition.ColorHex, IsNeedsReviewEntry = false,
            });
        }

        SelectedCategory = Categories.FirstOrDefault(c =>
            c.CategoryId == selectedId && c.IsNeedsReviewEntry == needsReview)
            ?? Categories[0];
    }

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
            // CHG-013：唯一通道 = 本机经典版 Outlook 登录态，无 OAuth 环节，直接探测 COM 可达性
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
        await _dispatcher.InvokeOnMainThreadAsync(() =>
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

    private async Task MarkReadAsync(string messageId, string category)
    {
        await _store.MarkReadAsync(messageId, CancellationToken.None);
        await _dispatcher.InvokeOnMainThreadAsync(() =>
        {
            // 左栏未读数联动（「全部」+ 对应类别；待确认入口计数与此无关）
            foreach (var item in Categories.Where(c =>
                         (c.CategoryId is null && !c.IsNeedsReviewEntry) || c.CategoryId == category))
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
            Category: SelectedCategory?.CategoryId,
            NeedsReviewOnly: SelectedCategory?.IsNeedsReviewEntry == true,
            UnreadOnly: UnreadOnly);
        var messages = await _store.GetInboxAsync(_accountId, query, ct);
        var nowUtc = DateTime.UtcNow;
        var unread = await _store.GetUnreadCountsAsync(_accountId, ct);
        var reviewCount = await _store.GetNeedsReviewCountAsync(_accountId, ClassificationService.DefaultReviewThreshold, ct);

        await _dispatcher.InvokeOnMainThreadAsync(() =>
        {
            ReplaceMails(messages);
            UnreadTotal = unread.Values.Sum(); // 托盘角标（FR-14）
            foreach (var item in Categories)
            {
                item.Count = item.IsNeedsReviewEntry
                    ? reviewCount
                    : item.CategoryId is { } c && unread.TryGetValue(c, out var n) ? n
                    : item.CategoryId is null ? messages.Count(m => !m.IsRead)
                    : 0; // S14-C：自定义类别无未读时归零
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

        var allCategory = Categories.First(c => c.CategoryId is null && !c.IsNeedsReviewEntry);
        if (SelectedCategory != allCategory)
        {
            SelectedCategory = allCategory; // 触发 LoadInboxAsync
            await Task.Delay(50, ct); // 等列表刷新（LoadInboxAsync 内部 invoke 队列）
        }
        else
        {
            await LoadInboxAsync(ct);
        }

        await _dispatcher.InvokeOnMainThreadAsync(() =>
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
        _dispatcher.Post(() =>
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
    public async Task ApplyCorrectionAsync(string newCategory, CancellationToken ct)
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

    private static string LabelOf(string category) => CategoryCatalog.LabelOf(category);

    /// <summary>阅读窗格内容：优先正文缓存 HTML，缺失时回退纯文本预览（04 §4.1 按需拉正文本期不做）。</summary>
    public async Task<string?> BuildReaderHtmlAsync(MailItemViewModel mail, CancellationToken ct)
    {
        if (mail.HtmlPath is { Length: > 0 } relative)
        {
            var html = await _bodyCache.ReadAsync(relative, ct);
            if (html is not null)
            {
                return InjectReaderChrome(html);
            }
        }

        var text = System.Net.WebUtility.HtmlEncode(mail.Preview);
        return InjectReaderChrome(
            $"<html><body><pre style=\"white-space:pre-wrap;font-family:inherit\">{text}</pre></body></html>");
    }

    /// <summary>S14-B 阅读窗格版式：邮件 HTML（营销模板常为固定宽表格）居中呈现——
    /// 页面浅灰底 + 正文白卡片对称留白，消除最大化后的右侧空白。构建期注入（WebView2 禁脚本）。</summary>
    private static string InjectReaderChrome(string html)
    {
        // S14 修正：WebView2 在复合 DPI 下 CSS 视口与控件宽可能不一致——不依赖视口值：
        // body 满宽（灰底）+ 直接子元素限宽居中（白卡片），overflow-x 裁剪，任何视口下都无横向溢出
        const string style = "<style>" +
            "html{background:#EEF2F7 !important;overflow-x:hidden !important;}" +
            "body{width:100% !important;margin:0 !important;padding:0 !important;" +
            "background:#EEF2F7 !important;min-height:100vh !important;" +
            "overflow-x:hidden !important;" +
            "font-family:Segoe UI,'Microsoft YaHei',sans-serif !important;}" +
            "body>*{width:100% !important;max-width:100% !important;margin:0 !important;" +
            "background:#fff !important;padding:22px 32px !important;" +
            "box-shadow:0 0 14px rgba(27,42,74,.06) !important;box-sizing:border-box !important;}" +
            "img,table{max-width:100% !important;height:auto !important;}" +
            "</style>";
        var headIndex = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headIndex < 0)
        {
            return style + html; // 无 head 的片段：样式块前置（浏览器可渲染）
        }

        var closeIndex = html.IndexOf('>', headIndex);
        return closeIndex < 0 ? style + html : html.Insert(closeIndex + 1, style);
    }
}
