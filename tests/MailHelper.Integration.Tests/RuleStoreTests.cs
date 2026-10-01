using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>rules 表与 classification_feedback 表仓储（04 §3.2 DDL；S8 反馈闭环持久化）。</summary>
public class RuleStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-rulestore-" + Guid.NewGuid().ToString("N"));
    private readonly RuleRepository _rules;
    private readonly FeedbackRepository _feedback;

    public RuleStoreTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(dbPath);
        _rules = new RuleRepository(dbPath);
        _feedback = new FeedbackRepository(dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static ClassifyRule Rule(string name, RuleSource source = RuleSource.Feedback, bool enabled = true,
        Importance? hint = null, Guid? id = null) => new(
        id ?? Guid.NewGuid(), name, RuleKind.SenderAddress, $"{name}@hku.hk",
        CategoryIds.Finance, hint, 10, 0, enabled, source);

    [Fact]
    public async Task Upsert_GetAll_Roundtrip()
    {
        var rule = Rule("feedback-rule", hint: Importance.P1);
        await _rules.UpsertAsync(rule, CancellationToken.None);

        var all = await _rules.GetAllAsync(CancellationToken.None);

        all.Should().ContainSingle();
        var row = all.Single();
        row.Id.Should().Be(rule.Id);
        row.Name.Should().Be(rule.Name);
        row.Kind.Should().Be(rule.Kind);
        row.Pattern.Should().Be(rule.Pattern);
        row.Category.Should().Be(CategoryIds.Finance);
        row.ImportanceHint.Should().Be(Importance.P1);
        row.Weight.Should().Be(10);
        row.Enabled.Should().BeTrue();
        row.Source.Should().Be(RuleSource.Feedback);
    }

    [Fact]
    public async Task Upsert_SameIdTwice_NoDuplicate()
    {
        var id = Guid.NewGuid();
        await _rules.UpsertAsync(Rule("r1", id: id), CancellationToken.None);
        await _rules.UpsertAsync(Rule("r1-renamed", id: id, enabled: false), CancellationToken.None);

        var all = await _rules.GetAllAsync(CancellationToken.None);
        all.Should().ContainSingle("同 Id 幂等 upsert（重放安全）");
        all.Single().Enabled.Should().BeFalse(); // 覆盖生效
    }

    [Fact]
    public async Task Feedback_Add_Counts()
    {
        // FK：classification_feedback.message_id → messages——先种账户与邮件行（生产顺序）
        var dbPath = Path.Combine(_dir, "mh.db");
        await new AccountRepository(dbPath).UpsertAccountAsync(new Account(
            "acc-1", "s@connect.hku.hk", "测试", ChannelKind.Graph.ToString(), ChannelKind.Graph, null,
            AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
        await new MailRepository(dbPath).UpsertRangeAsync("acc-1",
        [
            new MailMessage("m-1", "acc-1", "<m1@im>", "s", "f", "f@hku.hk", "p", null,
                new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), false, false,
                CategoryIds.Other, Importance.P2, 0.2, "rule", null, null, false),
        ], CancellationToken.None);

        var entry = new ClassificationFeedback(
            Guid.NewGuid(), "m-1", CategoryIds.Other, CategoryIds.Finance,
            Importance.P2, Importance.P1, new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc));

        await _feedback.AddAsync(entry, CancellationToken.None);
        await _feedback.AddAsync(entry, CancellationToken.None); // 同 id 重复提交幂等

        (await _feedback.CountAsync(CancellationToken.None)).Should().Be(1);
    }
}
