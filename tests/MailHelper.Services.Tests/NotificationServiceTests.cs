using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Logging;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>通知决策（04 §8 / FR-14）：P0 逐封、P1≥3 聚合、message_id 去重（AC2）、勿扰 queued 补发、首轮静默。
/// 真实 SQLite notification_log + FakeToastSender；TimeProvider 固定时刻使勿扰窗口可测。</summary>
public class NotificationServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-notify-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly NotificationRepository _store;
    private readonly FakeToastSender _sender = new();
    private readonly FakeSettingsStore _settings = new();

    public NotificationServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(_dbPath);
        // FK 链：messages.account_id → accounts、notification_log.message_id → messages（生产顺序：账户→邮件→通知日志）
        new AccountRepository(_dbPath).UpsertAccountAsync(new Account(
            "acc-1", "s@connect.hku.hk", "测试", "t1", ChannelKind.Graph, null,
            AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None)
            .GetAwaiter().GetResult();
        _store = new NotificationRepository(_dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private NotificationService NewService(TimeProvider? clock = null, ILogger<NotificationService>? logger = null) =>
        new(_store, _sender, _settings, logger, clock);

    private static MailMessage Mail(string id, Importance importance) => new(
        id, "acc-1", $"<{id}@im>", $"主题-{id}", "发件人", "someone@hku.hk", "预览", null,
        new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), false, false,
        CategoryIds.Finance, importance, 0.9, "rule",
        new DateTime(2026, 9, 27, 8, 0, 1, DateTimeKind.Utc), null, false);

    /// <summary>生产顺序：邮件先入 messages（FK notification_log.message_id → messages），再通知。</summary>
    private async Task SeedMessagesAsync(params MailMessage[] mails) =>
        await new MailRepository(_dbPath).UpsertRangeAsync("acc-1", mails, CancellationToken.None);

    private static FixedTimeProvider ClockAt(int hour) => new(new DateTimeOffset(2026, 9, 27, hour, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task P0_Single_SendsPerMailToast()
    {
        await SeedMessagesAsync(Mail("p0-1", Importance.P0));
        var outcome = await NewService().HandleNewMailsAsync([Mail("p0-1", Importance.P0)]);

        outcome.SentToasts.Should().Be(1);
        outcome.P0Count.Should().Be(1);
        _sender.Sent.Should().HaveCount(1);
        _sender.Sent[0].LaunchArgument.Should().Be("mailhelper://message/p0-1"); // FR-14 AC1 点击直达
        _sender.Sent[0].Title.Should().Contain("主题-p0-1");
    }

    [Fact]
    public async Task P0_Multiple_EachGetsToast()
    {
        await SeedMessagesAsync(Mail("a", Importance.P0), Mail("b", Importance.P0));
        var outcome = await NewService().HandleNewMailsAsync([Mail("a", Importance.P0), Mail("b", Importance.P0)]);

        outcome.SentToasts.Should().Be(2); // P0 逐封（04 §8.2）
        _sender.Sent.Should().OnlyContain(t => t.LaunchArgument!.StartsWith("mailhelper://message/"));
    }

    [Fact]
    public async Task P1_Three_AggregatedIntoOne()
    {
        var mails = new[] { Mail("p1a", Importance.P1), Mail("p1b", Importance.P1), Mail("p1c", Importance.P1) };
        await SeedMessagesAsync(mails);

        var outcome = await NewService().HandleNewMailsAsync(mails);

        outcome.SentToasts.Should().Be(1);
        outcome.Aggregated.Should().Be(1);
        _sender.Sent.Single().Body.Should().Contain("3"); // 「x 封重要邮件」摘要
    }

    [Fact]
    public async Task P1_Two_SentPerMail()
    {
        await SeedMessagesAsync(Mail("x", Importance.P1), Mail("y", Importance.P1));
        var outcome = await NewService().HandleNewMailsAsync([Mail("x", Importance.P1), Mail("y", Importance.P1)]);

        outcome.SentToasts.Should().Be(2); // <3 封逐封（04 §8.2）
    }

    [Fact]
    public async Task P2_P3_Ignored()
    {
        var outcome = await NewService().HandleNewMailsAsync([Mail("p2", Importance.P2), Mail("p3", Importance.P3)]);

        outcome.SentToasts.Should().Be(0); // 过滤 Importance >= P1（04 §8.1）
        _sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_NotificationSuppressed()
    {
        var svc = NewService();
        await SeedMessagesAsync(Mail("dup", Importance.P0));
        await svc.HandleNewMailsAsync([Mail("dup", Importance.P0)]);

        var second = await svc.HandleNewMailsAsync([Mail("dup", Importance.P0)]);

        second.SentToasts.Should().Be(0); // FR-14 AC2：message_id 去重
        _sender.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task QuietHours_QueuesWithoutToast()
    {
        _settings.Set(NotificationService.QuietHoursKey, "00:00-23:59"); // 全天勿扰
        var svc = NewService(ClockAt(12));
        await SeedMessagesAsync(Mail("q1", Importance.P0), Mail("q2", Importance.P1));

        var outcome = await svc.HandleNewMailsAsync([Mail("q1", Importance.P0), Mail("q2", Importance.P1)]);

        outcome.SentToasts.Should().Be(0);
        outcome.Queued.Should().Be(2); // 勿扰命中 → level=queued（04 §8.3）
        _sender.Sent.Should().BeEmpty();
        (await _store.CountQueuedAsync(CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task QuietHoursEnd_FlushesSummary()
    {
        _settings.Set(NotificationService.QuietHoursKey, "00:00-23:59");
        await SeedMessagesAsync(Mail("q1", Importance.P0), Mail("q2", Importance.P1));
        await NewService(ClockAt(12)).HandleNewMailsAsync([Mail("q1", Importance.P0), Mail("q2", Importance.P1)]);

        _settings.Set(NotificationService.QuietHoursKey, ""); // 勿扰结束
        var outcome = await NewService(ClockAt(18)).HandleNewMailsAsync([]);

        outcome.Flushed.Should().Be(2);
        outcome.SentToasts.Should().Be(1); // 补发一条摘要
        _sender.Sent.Single().Body.Should().Contain("2");
        (await _store.CountQueuedAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task DisabledP0_SkipsP0Mails()
    {
        _settings.Set(NotificationService.P0EnabledKey, "false");

        await SeedMessagesAsync(Mail("p0", Importance.P0));
        var outcome = await NewService().HandleNewMailsAsync([Mail("p0", Importance.P0)]);

        outcome.SentToasts.Should().Be(0);
        _sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task InitialRound_RegistersSilently()
    {
        await SeedMessagesAsync(Mail("i1", Importance.P0), Mail("i2", Importance.P1));
        var outcome = await NewService().HandleNewMailsAsync(
            [Mail("i1", Importance.P0), Mail("i2", Importance.P1)], initialRound: true);

        outcome.SentToasts.Should().Be(0); // 首轮不轰炸（D-50）
        (await _store.FilterUnnotifiedAsync(["i1", "i2"], CancellationToken.None)).Should().BeEmpty(); // 已登记，后续不重发
    }

    [Fact]
    public async Task Mixed_P0AndThreeP1_YieldsPerMailPlusAggregate()
    {
        var mails = new[] { Mail("p0", Importance.P0), Mail("p1a", Importance.P1), Mail("p1b", Importance.P1), Mail("p1c", Importance.P1) };
        await SeedMessagesAsync(mails);

        var outcome = await NewService().HandleNewMailsAsync(mails);

        outcome.SentToasts.Should().Be(2); // TC-018：P0 逐封 1 条 + P1 聚合 1 条
    }

    [Fact]
    public async Task NotifySent_EmitsStructuredLog()
    {
        var logDir = Path.Combine(_dir, "logs");
        var factory = MailHelperLogging.CreateFileLoggerFactory(logDir);
        var logger = factory.CreateLogger<NotificationService>();

        await SeedMessagesAsync(Mail("p0", Importance.P0));
        await NewService(logger: logger).HandleNewMailsAsync([Mail("p0", Importance.P0)]);
        factory.Dispose(); // flush（Serilog 文件句柄释放后才能断言）

        // 总控指令六.第 6 步：Serilog 文件断言 notify.sent 埋点（仅计数，无主题/发件人——红线⑤）
        var logFile = Directory.EnumerateFiles(logDir, "*.log", SearchOption.AllDirectories).Single();
        var content = File.ReadAllText(logFile);
        content.Should().Contain("notify.sent").And.NotContain("主题-p0").And.NotContain("someone@hku.hk");
    }

    private sealed class FakeToastSender : IToastSender
    {
        public List<ToastNotification> Sent { get; } = [];

        public Task SendAsync(ToastNotification notification, CancellationToken ct)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _data = [];

        public void Set(string key, string value) => _data[key] = value;

        public Task<string?> GetAsync(string key, CancellationToken ct) =>
            Task.FromResult(_data.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value, CancellationToken ct)
        {
            _data[key] = value;
            return Task.CompletedTask;
        }
    }
}
