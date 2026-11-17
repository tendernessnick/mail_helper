using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Concurrent;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Sync.OutlookMac;
using Microsoft.Extensions.Logging;

namespace MailHelper.ViewModels;

/// <summary>邮件列表行（05 §3.2 中栏行结构：重要度色条/徽章、发件人、主题、摘要、时间、附件图标）。</summary>
public partial class MailItemViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Subject { get; init; }

    public required string FromDisplay { get; init; }

    public string FromAddress { get; init; } = string.Empty;

    public string ReceivedFullText { get; init; } = string.Empty;

    public string CategoryLabel => CategoryCatalog.LabelOf(Category);

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

    /// <summary>重要度色值（05 §5.1 浅色卡；MS5 Avalonia 行模板消费，WPF 用自有转换器忽略本属性）。</summary>
    public string ImportanceColor => Importance switch
    {
        Importance.P0 => "#D13438",
        Importance.P1 => "#F7630C",
        Importance.P2 => "#0078D4",
        _ => "#8A8886",
    };

    /// <summary>重要度徽章文案（05 §7 无障碍：徽章附可读名）。</summary>
    public string ImportanceLabel => Importance switch
    {
        Importance.P0 => "P0 紧急",
        Importance.P1 => "P1 重要",
        Importance.P2 => "P2 普通",
        _ => "P3 低",
    };

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
        FromAddress = message.FromAddress ?? string.Empty,
        ReceivedFullText = message.ReceivedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
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
            : local.Year != today.Year ? local.ToString("yyyy-MM-dd")
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
    private readonly FeedbackService _feedback;
    private readonly SearchService _search;
    private readonly IMessageStore _store;
    private readonly ISettingsStore _settings;
    private readonly IBodyCache _bodyCache;
    private readonly IAccountStore _accounts;
    private readonly IMailProvider _provider;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IMainThreadDispatcher _dispatcher;

    private string? _accountId;
    private const int PageSize = 200;
    private int _visibleLimit = PageSize;
    private long _requestVersion;
    private CancellationTokenSource? _listRequest;
    private string _activeSearchText = string.Empty;
    private bool _updatingMails;
    private bool _manualSyncInProgress;
    private string? _notificationMailId;
    private readonly SemaphoreSlim _sortPreferenceGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _pendingReads = new();
    private readonly ConcurrentDictionary<string, byte> _locallyRead = new();
    private long _readVersion;

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
        _feedback = feedback;
        _search = search;
        _store = store;
        _settings = settings;
        _bodyCache = bodyCache;
        _accounts = accounts;
        _provider = provider;
        _logger = logger;
        _dispatcher = dispatcher;

        RebuildCategories();

        _sync.StateChanged += (_, e) => OnSyncStateChanged(e);
        _sync.SyncRoundCompleted += (_, _) =>
        {
            if (!_manualSyncInProgress)
            {
                _dispatcher.Post(() => _ = LoadInboxAsync(CancellationToken.None));
            }
        };
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
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string syncStatusText = "尚未同步";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool unreadOnly;

    /// <summary>S19/CHG-016：收件箱按时间排序（false=重要度优先，历史默认）。</summary>
    [ObservableProperty]
    private bool sortByDate;

    [ObservableProperty]
    private bool isSearchMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool hasMore;

    [ObservableProperty]
    private string listSummaryText = "尚未加载邮件";

    [ObservableProperty]
    private string emptyStateText = "该分类暂无邮件";

    /// <summary>全部未读数（托盘角标数据源，FR-14 角标计数）。</summary>
    [ObservableProperty]
    private int unreadTotal;

    // —— 视图导航（05 §2 Shell：收件箱/日程/规则/设置；S18 新增日程页）——
    [ObservableProperty]
    private bool isInboxView = true;

    [ObservableProperty]
    private bool isScheduleView;

    [ObservableProperty]
    private bool isRulesView;

    [ObservableProperty]
    private bool isSettingsView;

    [RelayCommand]
    private void ShowInbox() => SwitchView(0);

    [RelayCommand]
    private void ShowSchedule() => SwitchView(1);

    [RelayCommand]
    private void ShowRules() => SwitchView(2);

    [RelayCommand]
    private void ShowSettings() => SwitchView(3);

    private void SwitchView(int view)
    {
        IsInboxView = view == 0;
        IsScheduleView = view == 1;
        IsRulesView = view == 2;
        IsSettingsView = view == 3;
    }

    public event EventHandler<MailItemViewModel?>? SelectedMailHtmlNeeded;

    partial void OnSelectedMailChanged(MailItemViewModel? value)
    {
        if (_updatingMails)
        {
            return;
        }

        if (value is not null && !value.IsRead)
        {
            value.IsRead = true;
            _locallyRead[value.Id] = 0;
            Interlocked.Increment(ref _readVersion);
            AdjustUnreadCounts(value.Category, -1);
            _ = MarkReadAsync(value.Id, value.Category);
        }

        SelectedMailHtmlNeeded?.Invoke(this, value);
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        // S19：先读排序偏好再加载列表（属性变更钩子在 _accountId 就绪前不触发重复加载）
        SortByDate = await _settings.GetAsync(SettingsService.KeyInboxSort, ct) == "date";
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
            catch (MacChannelException macEx)
            {
                // MS8（CHG-014 §6.5）：Mac 通道四类错误的用户可读文案（TCC 引导/New Outlook 切换等）
                _logger.LogWarning(macEx, "ui.outlook_connect_failed err_code={ErrCode}", macEx.ErrorCode);
                SyncStatusText = $"连接失败：{macEx.Message}";
                return;
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
        _manualSyncInProgress = true;
        try
        {
            await _sync.SyncNowAsync(ct: ct);
            await LoadInboxAsync(ct);
        }
        finally
        {
            _manualSyncInProgress = false;
            IsBusy = false;
        }
    }

    partial void OnSelectedCategoryChanged(CategoryItemViewModel? value)
    {
        _notificationMailId = null;
        _visibleLimit = PageSize;
        _ = LoadInboxAsync(CancellationToken.None); // 左栏点击即过滤（05 §3.2）
    }

    /// <summary>S19/CHG-016：排序偏好切换 → 持久化 + 即时刷新（Initialize 读偏好时不触发：_accountId 未就绪）。</summary>
    partial void OnSortByDateChanged(bool value)
    {
        _notificationMailId = null;
        _ = SaveSortPreferenceAsync(value);
        if (_accountId is not null)
        {
            _visibleLimit = PageSize;
            _ = LoadInboxAsync(CancellationToken.None);
        }
    }

    private async Task SaveSortPreferenceAsync(bool value)
    {
        await _sortPreferenceGate.WaitAsync();
        try
        {
            await _settings.SetAsync(SettingsService.KeyInboxSort, value ? "date" : "importance", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ui.sort_preference_save_failed");
            await _dispatcher.InvokeOnMainThreadAsync(() => SyncStatusText = "排序已切换，但偏好保存失败；重启后可能恢复默认。");
        }
        finally
        {
            _sortPreferenceGate.Release();
        }
    }

    [RelayCommand]
    private async Task SearchAsync(CancellationToken ct)
    {
        if (_accountId is null)
        {
            return;
        }

        SwitchView(0);
        _notificationMailId = null;
        _activeSearchText = SearchText.Trim();
        IsSearchMode = _activeSearchText.Length > 0;
        _visibleLimit = PageSize;
        await LoadInboxAsync(ct);
    }

    [RelayCommand]
    private async Task ClearSearchAsync(CancellationToken ct)
    {
        SearchText = string.Empty;
        _notificationMailId = null;
        _activeSearchText = string.Empty;
        IsSearchMode = false;
        _visibleLimit = PageSize;
        await LoadInboxAsync(ct);
    }

    [RelayCommand]
    private async Task ToggleUnreadOnlyAsync(CancellationToken ct)
    {
        _notificationMailId = null;
        _visibleLimit = PageSize;
        await LoadInboxAsync(ct);
    }

    private bool CanLoadMore() => HasMore && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync(CancellationToken ct)
    {
        _visibleLimit += PageSize;
        await LoadInboxAsync(ct);
    }

    public async Task RunInitialSyncAsync(CancellationToken ct)
    {
        await SyncNowAsync(ct);
    }

    private async Task MarkReadAsync(string messageId, string category)
    {
        _pendingReads[messageId] = 0;
        try
        {
            await _store.MarkReadAsync(messageId, CancellationToken.None);
            Interlocked.Increment(ref _readVersion);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ui.mark_read_failed");
            _locallyRead.TryRemove(messageId, out _);
            Interlocked.Increment(ref _readVersion);
            await _dispatcher.InvokeOnMainThreadAsync(() =>
            {
                var item = Mails.FirstOrDefault(m => m.Id == messageId);
                if (item is not null)
                {
                    item.IsRead = false;
                }

                AdjustUnreadCounts(category, 1);
                SyncStatusText = "标记已读失败，已恢复未读状态；请稍后重试。";
            });
        }
        finally
        {
            _pendingReads.TryRemove(messageId, out _);
        }

        await RefreshUnreadCountsAsync();
    }

    private async Task RefreshUnreadCountsAsync()
    {
        if (_accountId is null)
        {
            return;
        }

        var version = Interlocked.Read(ref _readVersion);
        try
        {
            var unread = await _store.GetUnreadCountsAsync(_accountId, CancellationToken.None);
            var review = await _store.GetNeedsReviewCountAsync(_accountId, ClassificationService.DefaultReviewThreshold, CancellationToken.None);
            await _dispatcher.InvokeOnMainThreadAsync(() =>
            {
                if (version == Interlocked.Read(ref _readVersion))
                {
                    UpdateUnreadCounts(unread, review);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ui.unread_counts_refresh_failed");
        }
    }

    private void UpdateUnreadCounts(IReadOnlyDictionary<string, int> unread, int reviewCount)
    {
        UnreadTotal = unread.Values.Sum();
        foreach (var item in Categories)
        {
            item.Count = item.IsNeedsReviewEntry ? reviewCount
                : item.CategoryId is { } category ? unread.GetValueOrDefault(category)
                : UnreadTotal;
        }
    }

    private void AdjustUnreadCounts(string category, int delta)
    {
        UnreadTotal = Math.Max(0, UnreadTotal + delta);
        foreach (var item in Categories.Where(c =>
                     (c.CategoryId is null && !c.IsNeedsReviewEntry) || c.CategoryId == category))
        {
            item.Count = Math.Max(0, item.Count + delta);
        }
    }

    private async Task LoadInboxAsync(CancellationToken ct)
    {
        if (_accountId is null)
        {
            return;
        }

        var request = Interlocked.Increment(ref _requestVersion);
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var previous = _listRequest;
        _listRequest = source;
        previous?.Cancel();
        var token = source.Token;
        var visibleLimit = _visibleLimit;
        var searchText = _activeSearchText;
        var notificationId = _notificationMailId;
        var readVersion = Interlocked.Read(ref _readVersion);
        var categoryLabel = SelectedCategory?.Label ?? "收件箱（全部）";
        var query = new InboxQuery(
            Category: SelectedCategory?.CategoryId,
            NeedsReviewOnly: SelectedCategory?.IsNeedsReviewEntry == true,
            UnreadOnly: UnreadOnly,
            Limit: visibleLimit + 1,
            Sort: SortByDate ? InboxSortMode.Date : InboxSortMode.Importance); // S19/CHG-016
        IsLoading = true;
        EmptyStateText = "正在加载邮件…";
        try
        {
            var messages = searchText.Length > 0
                ? await _search.SearchAsync(searchText, query, visibleLimit + 1, token)
                : await _store.GetInboxAsync(_accountId, query, token);
            var unread = await _store.GetUnreadCountsAsync(_accountId, token);
            var reviewCount = await _store.GetNeedsReviewCountAsync(_accountId, ClassificationService.DefaultReviewThreshold, token);
            var notificationMail = notificationId is not null
                ? await _store.GetByIdAsync(notificationId, token) : null;
            await _dispatcher.InvokeOnMainThreadAsync(() =>
            {
                if (request != _requestVersion || token.IsCancellationRequested)
                {
                    return;
                }

                HasMore = messages.Count > visibleLimit;
                var visible = messages.Take(visibleLimit).ToList();
                if (notificationMail is { IsDeletedRemote: false } && notificationMail.AccountId == _accountId &&
                    !visible.Any(m => m.Id == notificationMail.Id))
                {
                    visible.Insert(0, notificationMail);
                }

                ReplaceMails(visible);
                if (readVersion == Interlocked.Read(ref _readVersion))
                {
                    UpdateUnreadCounts(unread, reviewCount);
                }

                ListSummaryText = $"{categoryLabel}{(searchText.Length > 0 ? $" · 搜索「{searchText}」" : string.Empty)}" +
                    $" · 已显示 {Mails.Count} 封{(HasMore ? "，可继续加载" : string.Empty)}";
                EmptyStateText = searchText.Length > 0 ? "没有符合当前搜索和筛选条件的邮件" : query.UnreadOnly
                    ? "当前分类没有未读邮件" : query.NeedsReviewOnly ? "没有待确认邮件" : "该分类暂无邮件";
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 新请求会取代旧请求，外部取消不清空已载入列表。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ui.inbox_load_failed");
            await _dispatcher.InvokeOnMainThreadAsync(() =>
            {
                if (request == _requestVersion)
                {
                    SyncStatusText = "邮件加载失败，请重试搜索或切换分类。";
                    EmptyStateText = "暂时无法加载邮件，请重试";
                    ListSummaryText = $"{categoryLabel} · 加载失败，保留上次列表";
                    HasMore = false;
                }
            });
        }
        finally
        {
            await _dispatcher.InvokeOnMainThreadAsync(() =>
            {
                if (request == _requestVersion)
                {
                    IsLoading = false;
                    _listRequest = null;
                    if (token.IsCancellationRequested)
                    {
                        EmptyStateText = "加载已取消，请重新搜索或切换分类";
                        ListSummaryText = "加载已取消，保留上次列表";
                    }
                }
            });
            source.Dispose();
        }
    }

    /// <summary>Toast 点击直达（FR-14 AC1，launch=mailhelper://message/{id}）：切回全部收件箱并选中该邮件。</summary>
    public async Task SelectMailByIdAsync(string messageId, CancellationToken ct)
    {
        if (_accountId is null)
        {
            return;
        }

        // 通知目标可能在首200封之外；直接按 ID 查询，不能靠固定等待和当前页碰运气。
        var message = await _store.GetByIdAsync(messageId, ct);
        if (message is null || message.AccountId != _accountId || message.IsDeletedRemote)
        {
            return;
        }

        SwitchView(0);
        SearchText = string.Empty;
        _activeSearchText = string.Empty;
        IsSearchMode = false;
        UnreadOnly = false;
        SelectedCategory = Categories.First(c => c.CategoryId is null && !c.IsNeedsReviewEntry);
        _notificationMailId = messageId;
        await LoadInboxAsync(ct);

        await _dispatcher.InvokeOnMainThreadAsync(() =>
        {
            var item = Mails.FirstOrDefault(m => m.Id == messageId);
            if (item is null)
            {
                item = MailItemViewModel.From(message, DateTime.UtcNow);
                Mails.Insert(0, item);
            }

            SelectedMail = item;
        });
    }

    private void ReplaceMails(IReadOnlyList<MailMessage> messages)
    {
        var selectedId = SelectedMail?.Id;
        var previousSelection = SelectedMail;
        var byId = Mails.ToDictionary(m => m.Id);
        var desired = messages.Select(message =>
        {
            var next = MailItemViewModel.From(message, DateTime.UtcNow);
            if (message.IsRead)
            {
                _locallyRead.TryRemove(message.Id, out _); // 仓储已确认已读，不再保留临时乐观状态。
            }

            if (_pendingReads.ContainsKey(next.Id) || _locallyRead.ContainsKey(next.Id))
            {
                next.IsRead = true;
            }

            if (byId.TryGetValue(next.Id, out var old) && SameContent(old, next))
            {
                old.IsRead = next.IsRead;
                return old;
            }

            return next;
        }).ToList();

        _updatingMails = true;
        try
        {
            for (var i = 0; i < desired.Count; i++)
            {
                if (i < Mails.Count && ReferenceEquals(Mails[i], desired[i]))
                {
                    continue;
                }

                var existingIndex = Mails.IndexOf(desired[i]);
                if (existingIndex >= 0)
                {
                    Mails.Move(existingIndex, i);
                }
                else
                {
                    Mails.Insert(i, desired[i]);
                }
            }

            while (Mails.Count > desired.Count)
            {
                Mails.RemoveAt(Mails.Count - 1);
            }

            SelectedMail = desired.FirstOrDefault(m => m.Id == selectedId);
        }
        finally
        {
            _updatingMails = false;
        }

        if (!ReferenceEquals(previousSelection, SelectedMail))
        {
            SelectedMailHtmlNeeded?.Invoke(this, SelectedMail);
        }
    }

    private static bool SameContent(MailItemViewModel a, MailItemViewModel b) =>
        a.Subject == b.Subject && a.FromDisplay == b.FromDisplay && a.FromAddress == b.FromAddress &&
        a.ReceivedFullText == b.ReceivedFullText && a.Preview == b.Preview &&
        a.ReceivedText == b.ReceivedText && a.Category == b.Category && a.Importance == b.Importance &&
        a.Confidence == b.Confidence && a.HtmlPath == b.HtmlPath && a.HasAttachments == b.HasAttachments;

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

    /// <summary>WPF 阅读纸张配色由已应用的窗口主题提供；Avalonia 使用自己的原生文本阅读器。</summary>
    public bool ReaderUsesDarkColors { get; set; }

    /// <summary>优先读取缓存 HTML，缺失时安全编码纯文本预览。</summary>
    public async Task<string?> BuildReaderHtmlAsync(MailItemViewModel mail, CancellationToken ct)
    {
        if (mail.HtmlPath is { Length: > 0 } relative)
        {
            var html = await _bodyCache.ReadAsync(relative, ct);
            if (html is not null)
            {
                return MailHelper.Infrastructure.Reading.ReaderDocument.Build(html, ReaderUsesDarkColors);
            }
        }

        var text = System.Net.WebUtility.HtmlEncode(mail.Preview);
        return MailHelper.Infrastructure.Reading.ReaderDocument.Build(
            $"<html><body><pre style=\"white-space:pre-wrap;font-family:inherit;text-align:left;line-height:1.8\">{text}</pre></body></html>", ReaderUsesDarkColors);
    }

}
