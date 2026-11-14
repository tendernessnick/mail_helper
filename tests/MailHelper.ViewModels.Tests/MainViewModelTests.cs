using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using MailHelper.Infrastructure.Sync;
using MailHelper.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailHelper.ViewModels.Tests;

/// <summary>共享VM使用独立临时SQLite和假同步源；不访问本机Outlook或真实邮件。</summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-vm-" + Guid.NewGuid().ToString("N"));
    private readonly AccountRepository _accounts;
    private readonly SettingsRepository _settings;
    private readonly MailRepository _repository;
    private readonly ControlledStore _store;
    private readonly FakeMailProvider _provider = new();
    private readonly MainViewModel _vm;

    public MainViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "test.db");
        MailDatabase.EnsureReady(dbPath);
        _accounts = new AccountRepository(dbPath);
        _settings = new SettingsRepository(dbPath);
        _repository = new MailRepository(dbPath);
        _store = new ControlledStore(_repository);
        var rules = new RuleEngine(RuleSet.Empty);
        _vm = new MainViewModel(
            new SyncCoordinator(_provider, _store, _accounts, "acc-1"),
            new ClassificationService(rules, _store, "acc-1"),
            new FeedbackService(_store, new RuleRepository(dbPath), new FeedbackRepository(dbPath), rules),
            new SearchService(_store, "acc-1"), _store, _settings,
            new BodyCacheStore(Path.Combine(_dir, "bodies")), _accounts, _provider,
            NullLogger<MainViewModel>.Instance, false, new ImmediateDispatcher());
    }

    private static MailMessage Message(int number, string category = CategoryIds.Course, bool read = false, string subject = "exam deadline") =>
        new($"mail-{number:D4}", "acc-1", null, subject, "Sender", "sender@hku.hk", "preview", null,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(number), false, read,
            category, Importance.P1, 0.9, "rule", DateTime.UtcNow, null, false);

    private async Task ConnectAsync(params MailMessage[] messages)
    {
        await _accounts.UpsertAccountAsync(new Account("acc-1", "test@hku.hk", null, null,
            ChannelKind.OutlookDesktop, null, AccountStatus.Active, DateTime.UtcNow), CancellationToken.None);
        await _repository.UpsertRangeAsync("acc-1", messages, CancellationToken.None);
        _vm.PrivacyAccepted = true;
        await _vm.ConnectCommand.ExecuteAsync(null);
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    [Fact]
    public async Task LargeInbox_LoadsMore_PreservesSelection_AndUsesGlobalUnreadTotal()
    {
        await ConnectAsync(Enumerable.Range(0, 405).Select(i => Message(i)).ToArray());
        _vm.Mails.Should().HaveCount(200);
        _vm.HasMore.Should().BeTrue();
        _vm.UnreadTotal.Should().Be(405);
        _vm.Categories[0].Count.Should().Be(405, "全部徽章不可取当前页未读数");
        var selected = _vm.Mails[10];
        _vm.SelectedMail = selected;
        await UntilAsync(() => _vm.UnreadTotal == 404);
        await _vm.LoadMoreCommand.ExecuteAsync(null);
        _vm.Mails.Should().HaveCount(400);
        _vm.SelectedMail.Should().BeSameAs(selected, "加载更多不打断阅读");
        await _vm.LoadMoreCommand.ExecuteAsync(null);
        _vm.Mails.Should().HaveCount(405);
        _vm.Mails.Select(m => m.Id).Should().OnlyHaveUniqueItems();
        _vm.HasMore.Should().BeFalse();
        _vm.LoadMoreCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Search_RemainsActive_AcrossUnreadSortCategoryAndSync()
    {
        await ConnectAsync(Message(1), Message(2, read: true), Message(3, CategoryIds.Finance),
            Message(4, subject: "unrelated message"));
        _vm.ShowRulesCommand.Execute(null);
        _vm.SearchText = "exam";
        await _vm.SearchCommand.ExecuteAsync(null);
        _vm.IsInboxView.Should().BeTrue();
        _vm.Mails.Should().HaveCount(3);
        _vm.UnreadOnly = true;
        await _vm.ToggleUnreadOnlyCommand.ExecuteAsync(null);
        _vm.Mails.Should().HaveCount(2);
        _vm.IsSearchMode.Should().BeTrue();
        _vm.SortByDate = true;
        await UntilAsync(() => !_vm.IsLoading);
        _vm.SelectedCategory = _vm.Categories.Single(c => c.CategoryId == CategoryIds.Course);
        await UntilAsync(() => !_vm.IsLoading);
        _vm.Mails.Select(m => m.Id).Should().Equal("mail-0001");
        await _vm.SyncNowCommand.ExecuteAsync(null);
        _vm.Mails.Select(m => m.Id).Should().Equal("mail-0001");
        _vm.ListSummaryText.Should().Contain("exam").And.Contain("课程学习");
        await _vm.ClearSearchCommand.ExecuteAsync(null);
        _vm.Mails.Select(m => m.Id).Should().Equal("mail-0004", "mail-0001");
        _vm.IsSearchMode.Should().BeFalse();
    }

    [Fact]
    public async Task SlowOldCategoryRequest_CannotOverwriteLatestCategory()
    {
        await ConnectAsync(Message(1), Message(2, CategoryIds.Finance));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.AfterInbox = async query =>
        {
            if (query.Category == CategoryIds.Course)
            {
                entered.TrySetResult();
                await release.Task;
            }
        };
        _vm.SelectedCategory = _vm.Categories.Single(c => c.CategoryId == CategoryIds.Course);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _vm.SelectedCategory = _vm.Categories.Single(c => c.CategoryId == CategoryIds.Finance);
        await UntilAsync(() => !_vm.IsLoading);
        release.TrySetResult();
        await UntilAsync(() => _store.CompletedDelayedRequests > 0);
        _vm.Mails.Select(m => m.Id).Should().Equal("mail-0002");
        _vm.ListSummaryText.Should().Contain("财务缴费");
    }

    [Fact]
    public async Task ClearingSearch_InvalidatesSlowSearchResult()
    {
        await ConnectAsync(Message(1), Message(2, subject: "other"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.AfterSearch = async () => { entered.TrySetResult(); await release.Task; };
        _vm.SearchText = "exam";
        var searching = _vm.SearchCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _vm.ClearSearchCommand.ExecuteAsync(null);
        release.TrySetResult();
        await searching;
        _vm.Mails.Should().HaveCount(2);
        _vm.IsSearchMode.Should().BeFalse();
        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task FailedRead_RestoresUnreadStateAndCounts()
    {
        await ConnectAsync(Message(1));
        _store.MarkReadError = new IOException("injected storage failure");
        _vm.SelectedMail = _vm.Mails[0];
        await UntilAsync(() => _vm.SyncStatusText.Contains("标记已读失败", StringComparison.Ordinal));
        _vm.Mails[0].IsRead.Should().BeFalse();
        _vm.UnreadTotal.Should().Be(1);
        _vm.Categories[0].Count.Should().Be(1);
        _vm.Categories.Single(c => c.CategoryId == CategoryIds.Course).Count.Should().Be(1);
    }

    [Fact]
    public async Task ToastTarget_OutsideFirstPage_IsSelectedWithoutArbitraryDelay()
    {
        await ConnectAsync(Enumerable.Range(0, 405).Select(i => Message(i)).ToArray());
        _vm.ShowScheduleCommand.Execute(null);
        await _vm.SelectMailByIdAsync("mail-0000", CancellationToken.None);
        _vm.IsInboxView.Should().BeTrue();
        _vm.SelectedMail?.Id.Should().Be("mail-0000");
        _vm.Mails.Should().Contain(_vm.SelectedMail!);
        _vm.SelectedMail!.IsRead.Should().BeTrue();
        await _vm.SyncNowCommand.ExecuteAsync(null);
        _vm.SelectedMail?.Id.Should().Be("mail-0000", "周期或手动刷新仍保留通知定位的邮件");
    }

    [Fact]
    public async Task Refresh_ReplacesChangedSelectedContent_AndClearsStaleReaderWhenFilteredOut()
    {
        await ConnectAsync(Message(1), Message(2, CategoryIds.Finance));
        _vm.SelectedMail = _vm.Mails.Single(m => m.Id == "mail-0001");
        await UntilAsync(() => _vm.UnreadTotal == 1);
        await _vm.ApplyCorrectionAsync(CategoryIds.Career, CancellationToken.None);
        _vm.SelectedMail!.Category.Should().Be(CategoryIds.Career);
        MailItemViewModel? reader = _vm.SelectedMail;
        _vm.SelectedMailHtmlNeeded += (_, mail) => reader = mail;
        _vm.SelectedCategory = _vm.Categories.Single(c => c.CategoryId == CategoryIds.Finance);
        await UntilAsync(() => !_vm.IsLoading);
        _vm.SelectedMail.Should().BeNull();
        reader.Should().BeNull();
    }

    [Fact]
    public async Task LoadFailure_PreservesCurrentList_AndReportsRecoverableError()
    {
        await ConnectAsync(Message(1));
        _store.InboxError = new IOException("injected read failure");
        await _vm.ToggleUnreadOnlyCommand.ExecuteAsync(null);
        _vm.Mails.Should().HaveCount(1);
        _vm.IsLoading.Should().BeFalse();
        _vm.SyncStatusText.Should().Contain("加载失败");
        _vm.ListSummaryText.Should().Contain("保留上次列表");
        _store.InboxError = null;
        await _vm.ToggleUnreadOnlyCommand.ExecuteAsync(null);
        _vm.Mails.Should().HaveCount(1);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class ImmediateDispatcher : IMainThreadDispatcher
    {
        public Task InvokeOnMainThreadAsync(Action action) { action(); return Task.CompletedTask; }
        public void Post(Action action) => action();
    }

    private sealed class ControlledStore(IMessageStore inner) : IMessageStore
    {
        public Func<InboxQuery, Task>? AfterInbox { get; set; }
        public Func<Task>? AfterSearch { get; set; }
        public Exception? InboxError { get; set; }
        public Exception? MarkReadError { get; set; }
        public int CompletedDelayedRequests { get; private set; }
        public async Task<IReadOnlyList<MailMessage>> GetInboxAsync(string accountId, InboxQuery query, CancellationToken ct)
        {
            if (InboxError is { } failure) { throw failure; }
            var rows = await inner.GetInboxAsync(accountId, query, ct);
            if (AfterInbox is { } callback) { await callback(query); CompletedDelayedRequests++; }
            return rows; // 故意模拟无法中途取消的提供者，VM仍须校验请求版本。
        }
        public async Task<IReadOnlyList<MailMessage>> SearchAsync(string accountId, SearchQuery query, int limit, CancellationToken ct)
        {
            var rows = await inner.SearchAsync(accountId, query, limit, ct);
            if (AfterSearch is { } callback) { await callback(); }
            return rows;
        }
        public Task MarkReadAsync(string id, CancellationToken ct) => MarkReadError is { } failure ? Task.FromException(failure) : inner.MarkReadAsync(id, ct);
        public Task<int> UpsertRangeAsync(string accountId, IReadOnlyList<MailMessage> messages, CancellationToken ct) => inner.UpsertRangeAsync(accountId, messages, ct);
        public Task<MailMessage?> GetByIdAsync(string id, CancellationToken ct) => inner.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<MailMessage>> GetPendingClassificationAsync(string accountId, int batchSize, CancellationToken ct) => inner.GetPendingClassificationAsync(accountId, batchSize, ct);
        public Task ApplyClassificationRangeAsync(IReadOnlyList<ClassificationWrite> writes, CancellationToken ct) => inner.ApplyClassificationRangeAsync(writes, ct);
        public Task<int> ResetRuleClassificationAsync(string accountId, CancellationToken ct) => inner.ResetRuleClassificationAsync(accountId, ct);
        public Task<IReadOnlyList<MailMessage>> SearchFtsAsync(string accountId, string query, int limit, CancellationToken ct) => inner.SearchFtsAsync(accountId, query, limit, ct);
        public Task<IReadOnlyDictionary<string, int>> GetUnreadCountsAsync(string accountId, CancellationToken ct) => inner.GetUnreadCountsAsync(accountId, ct);
        public Task<int> GetNeedsReviewCountAsync(string accountId, double threshold, CancellationToken ct) => inner.GetNeedsReviewCountAsync(accountId, threshold, ct);
        public Task<IReadOnlyList<MailMessage>> GetDdlCandidatesAsync(string accountId, int batchSize, CancellationToken ct) => inner.GetDdlCandidatesAsync(accountId, batchSize, ct);
        public Task MarkDdlScannedAsync(IReadOnlyList<string> ids, DateTime at, CancellationToken ct) => inner.MarkDdlScannedAsync(ids, at, ct);
    }
}
