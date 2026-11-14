using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using MailHelper.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Services.Tests;

public sealed class SyncCompletionServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-completion-" + Guid.NewGuid().ToString("N"));
    private readonly MailRepository _messages;
    private readonly AccountRepository _accounts;
    private readonly ScheduleRepository _schedule;
    private readonly RecordingSender _sender = new();
    private readonly SyncCompletionService _completion;

    public SyncCompletionServiceTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "mail.db");
        MailDatabase.EnsureReady(dbPath);
        _messages = new MailRepository(dbPath);
        _accounts = new AccountRepository(dbPath);
        _schedule = new ScheduleRepository(dbPath);
        _accounts.UpsertAccountAsync(new Account("acc-1", "student@example.test", null, null,
            ChannelKind.OutlookDesktop, null, AccountStatus.Active, DateTime.UtcNow), default).GetAwaiter().GetResult();
        var settings = new SettingsRepository(dbPath);
        _completion = new SyncCompletionService(new ClassificationService(new ImportantClassifier(), _messages, "acc-1"),
            _messages, new NotificationService(new NotificationRepository(dbPath), _sender, settings),
            new ScheduleService(_messages, _schedule, new BodyCacheStore(Path.Combine(_dir, "bodies")),
                settings, _sender, "acc-1", clock: new FixedTimeProvider(new DateTime(2026, 10, 4, 4, 0, 0, DateTimeKind.Utc))));
    }

    [Fact]
    public async Task Sync_DrainsMoreThan200_ClassifiesBeforeEvent_AndInitialRoundIsSilent()
    {
        var provider = new FakeMailProvider { CompletedLink = "first" };
        provider.Pages.Add(Enumerable.Range(0, 205).Select(i => Remote(i.ToString())).ToArray());
        var coordinator = new SyncCoordinator(provider, _messages, _accounts, "acc-1")
        {
            ProcessRoundAsync = _completion.ProcessAsync,
        };
        var completed = false;
        coordinator.SyncRoundCompleted += (_, _) => completed = true;

        await coordinator.SyncNowAsync();

        coordinator.State.Should().Be(SyncState.Idle);
        completed.Should().BeTrue();
        (await _messages.GetPendingClassificationAsync("acc-1", 300, default)).Should().BeEmpty();
        (await _messages.GetByIdAsync("204", default))!.Importance.Should().Be(Importance.P1);
        _sender.Sent.Should().BeEmpty("首轮历史同步不弹出205条通知");

        provider.Pages.Clear();
        provider.Pages.Add([Remote("new-1"), Remote("new-2"), Remote("new-3")]);
        await coordinator.SyncNowAsync();
        _sender.Sent.Should().ContainSingle().Which.Body.Should().Contain("3");
        await coordinator.SyncNowAsync();
        _sender.Sent.Should().ContainSingle("重拉同批消息时通知去重");
    }

    [Fact]
    public async Task Sync_ExtractsMoreThan50CanvasMails_BeforeCompletionEvent()
    {
        var provider = new FakeMailProvider { CompletedLink = "canvas" };
        provider.Pages.Add(Enumerable.Range(1, 51).Select(i => Remote("canvas-" + i) with
        {
            FromAddress = "no-reply@canvas.cityu.edu.hk",
            BodyPreview = $"Assignment Created - Work {i}, IS6400 Business Data Analytics\ndue: Oct 9 at 11:59pm\nClick to view <https://canvas.cityu.edu.hk/courses/71457/assignments/{187000 + i}>",
        }).ToArray());
        var coordinator = new SyncCoordinator(provider, _messages, _accounts, "acc-1")
        {
            ProcessRoundAsync = _completion.ProcessAsync,
        };
        await coordinator.SyncNowAsync();
        (await _messages.GetDdlCandidatesAsync("acc-1", 100, default)).Should().BeEmpty();
        (await _schedule.GetAllAsync("acc-1", default)).Should().HaveCount(51);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotPreventScheduleExtraction()
    {
        _sender.ThrowOnSend = true;
        var mail = new MailMessage("canvas", "acc-1", "canvas@test", "Canvas", "Canvas",
            "no-reply@canvas.cityu.edu.hk",
            "Assignment Created - Work, IS6400 Business Data Analytics\ndue: Oct 9 at 11:59pm\nClick to view <https://canvas.cityu.edu.hk/courses/71457/assignments/187001>",
            null, DateTime.UtcNow, false, false, CategoryIds.Other, Importance.P2, null, "rule", null, null, false);
        await _messages.UpsertRangeAsync("acc-1", [mail], default);
        await _completion.ProcessAsync(new SyncRoundCompletedEventArgs([mail], false, 0), default);
        (await _schedule.GetAllAsync("acc-1", default)).Should().ContainSingle();
    }

    private static RemoteMessage Remote(string id) => new(id, id + "@test", "Interview", "Test", "test@example.test",
        "Details", null, new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc), false, false, ChangeKind.Added);

    private sealed class ImportantClassifier : IClassifier
    {
        public string Name => "test";
        public Task<ClassificationResult> ClassifyAsync(ClassifiedInput input, CancellationToken ct) =>
            Task.FromResult(new ClassificationResult(CategoryIds.Career, Importance.P1, 0.95, "rule"));
    }

    private sealed class RecordingSender : IToastSender
    {
        public List<ToastNotification> Sent { get; } = new();
        public bool ThrowOnSend { get; set; }
        public Task SendAsync(ToastNotification notification, CancellationToken ct)
        {
            if (ThrowOnSend) throw new InvalidOperationException("test toast unavailable");
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
