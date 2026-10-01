using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>纠正反馈闭环（FR-11 / 04 §2.2 FeedbackService 签名；TC-014 端到端）：
/// 改判立即生效（ClassifiedBy=user）→ 写 classification_feedback → upsert 发件人规则（source=Feedback）→ 引擎即时生效。</summary>
public class FeedbackServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-feedback-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly MailRepository _messages;
    private readonly RuleRepository _rules;
    private readonly FeedbackRepository _feedback;
    private readonly RuleEngine _engine;
    private readonly FeedbackService _service;

    public FeedbackServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(_dbPath);
        new AccountRepository(_dbPath).UpsertAccountAsync(NewAccount(), CancellationToken.None)
            .GetAwaiter().GetResult(); // FK 链：messages.account_id → accounts
        _messages = new MailRepository(_dbPath);
        _rules = new RuleRepository(_dbPath);
        _feedback = new FeedbackRepository(_dbPath);
        _engine = new RuleEngine(new RuleSet("test", new RuleScoring(), []));
        _service = new FeedbackService(_messages, _rules, _feedback, _engine);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Account NewAccount() => new(
        "acc-1", "s@connect.hku.hk", "测试", "t1", ChannelKind.Graph, null,
        AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc));

    private static MailMessage Mail(string id, string from, string category = CategoryIds.Other, bool classified = true) => new(
        id, "acc-1", $"<{id}@im>", $"主题-{id}", "发件人", from, "预览", null,
        new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), false, false,
        category, Importance.P2, 0.2, "rule",
        classified ? new DateTime(2026, 9, 27, 8, 0, 1, DateTimeKind.Utc) : null, null, false);

    private async Task<MailMessage> StoredAsync(MailMessage mail)
    {
        await _messages.UpsertRangeAsync("acc-1", [mail], CancellationToken.None);
        return mail;
    }

    [Fact]
    public async Task ApplyCorrection_UpdatesMailImmediate()
    {
        await StoredAsync(Mail("m1", "bursary@hku.hk"));

        var ok = await _service.ApplyCorrectionAsync("m1", CategoryIds.Finance, null, CancellationToken.None);

        ok.Should().BeTrue();
        var updated = (await _messages.GetInboxAsync("acc-1", new InboxQuery(), CancellationToken.None)).Single();
        updated.Category.Should().Be(CategoryIds.Finance); // FR-11：该邮件立即生效
        updated.ClassifiedBy.Should().Be("user");
        updated.Confidence.Should().Be(1.0);
    }

    [Fact]
    public async Task ApplyCorrection_WritesFeedbackRow()
    {
        await StoredAsync(Mail("m1", "bursary@hku.hk", CategoryIds.Other));

        await _service.ApplyCorrectionAsync("m1", CategoryIds.Finance, Importance.P1, CancellationToken.None);

        (await _feedback.CountAsync(CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task ApplyCorrection_UpsertsFeedbackRule_EngineHits()
    {
        await StoredAsync(Mail("m1", "bursary@hku.hk"));

        await _service.ApplyCorrectionAsync("m1", CategoryIds.Finance, null, CancellationToken.None);

        var stored = await _rules.GetAllAsync(CancellationToken.None);
        var rule = stored.Single();
        rule.Source.Should().Be(RuleSource.Feedback);
        rule.Kind.Should().Be(RuleKind.SenderAddress);
        rule.Pattern.Should().Be("bursary@hku.hk");
        rule.Category.Should().Be(CategoryIds.Finance);

        var hit = await _engine.ClassifyAsync(
            new ClassifiedInput("随便主题", "发件人", "bursary@hku.hk", null, null), CancellationToken.None);
        hit.Category.Should().Be(CategoryIds.Finance); // 引擎内存态即时生效（无需重启）
    }

    [Fact]
    public async Task ApplyCorrection_SameSenderTwice_SingleRuleRowUpdated()
    {
        await StoredAsync(Mail("m1", "bursary@hku.hk"));
        await StoredAsync(Mail("m2", "bursary@hku.hk"));

        await _service.ApplyCorrectionAsync("m1", CategoryIds.Finance, null, CancellationToken.None);
        await _service.ApplyCorrectionAsync("m2", CategoryIds.Career, null, CancellationToken.None);

        var stored = await _rules.GetAllAsync(CancellationToken.None);
        stored.Should().ContainSingle("同发件人重复改判应覆盖（生成或加权，04 §2.2）");
        stored.Single().Category.Should().Be(CategoryIds.Career);
    }

    [Fact]
    public async Task TC014_AfterCorrection_NewMailFromSameSender_GoesToNewCategory()
    {
        await StoredAsync(Mail("m1", "bursary@hku.hk"));
        await _service.ApplyCorrectionAsync("m1", CategoryIds.Finance, null, CancellationToken.None);

        // 同发件人新邮件（入库为未分类 other/P2 待分类）→ 分类管线 → 反馈规则生效
        await StoredAsync(Mail("m2-new", "bursary@hku.hk", classified: false)); // 新邮件：未分类待处理
        var classifier = new ClassificationService(_engine, _messages, "acc-1");
        await classifier.ClassifyPendingAsync(10, CancellationToken.None);

        var m2 = (await _messages.GetInboxAsync("acc-1", new InboxQuery(), CancellationToken.None))
            .Single(m => m.Id == "m2-new");
        m2.Category.Should().Be(CategoryIds.Finance); // TC-014：新邮件直接归新类别
        m2.Importance.Should().Be(Importance.P2); // 未给重要度建议：维持基准
    }

    [Fact]
    public async Task ApplyCorrection_WithNewImportance_UpdatesMailAndHint()
    {
        await StoredAsync(Mail("m1", "dean@hku.hk"));

        await _service.ApplyCorrectionAsync("m1", CategoryIds.Admin, Importance.P0, CancellationToken.None);

        var updated = (await _messages.GetInboxAsync("acc-1", new InboxQuery(), CancellationToken.None)).Single();
        updated.Category.Should().Be(CategoryIds.Admin);
        updated.Importance.Should().Be(Importance.P0);
        (await _rules.GetAllAsync(CancellationToken.None)).Single().ImportanceHint.Should().Be(Importance.P0);
    }

    [Fact]
    public async Task ApplyCorrection_UnknownMessage_ReturnsFalse()
    {
        var ok = await _service.ApplyCorrectionAsync("ghost", CategoryIds.Finance, null, CancellationToken.None);

        ok.Should().BeFalse();
        (await _feedback.CountAsync(CancellationToken.None)).Should().Be(0);
        (await _rules.GetAllAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyCorrection_LogsDomainOnly()
    {
        var logDir = Path.Combine(_dir, "logs");
        var factory = MailHelperLogging.CreateFileLoggerFactory(logDir);
        var service = new FeedbackService(
            _messages, _rules, _feedback, _engine, factory.CreateLogger<FeedbackService>());
        await StoredAsync(Mail("m1", "bursary@hku.hk"));

        await service.ApplyCorrectionAsync("m1", CategoryIds.Finance, null, CancellationToken.None);
        factory.Dispose(); // flush

        // 日志红线（04 §6）：发件人仅域名，不落完整地址
        var logFile = Directory.EnumerateFiles(logDir, "*.log", SearchOption.AllDirectories).Single();
        var content = File.ReadAllText(logFile);
        content.Should().Contain("feedback.rule_upserted");
        content.Should().NotContain("bursary@hku.hk");
        content.Should().Contain("hku.hk");
    }
}
