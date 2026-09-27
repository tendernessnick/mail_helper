using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;
using IClassifier = MailHelper.Core.Abstractions.IClassifier;

namespace MailHelper.Services.Tests;

/// <summary>分类管线（04 章 §2.2 ClassifyPendingAsync：预处理→分类→写回→汇总）。</summary>
public class ClassificationServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-classify-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly AccountRepository _accounts;

    public ClassificationServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(_dbPath);
        _accounts = new AccountRepository(_dbPath);
        _accounts.UpsertAccountAsync(new Account(
            "acc-1", "s@connect.hku.hk", "测试", "t1", ChannelKind.Graph, null,
            AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static IClassifier NewClassifier() => new RuleEngine(new RuleSet("test", new RuleScoring(), new[]
    {
        new ClassifyRule(Guid.NewGuid(), "Course-Kw", RuleKind.SubjectKeyword, "assignment|作业", MailCategory.Course, null, 3, 100, true, RuleSource.Builtin),
        new ClassifyRule(Guid.NewGuid(), "Career-Kw", RuleKind.SubjectKeyword, "interview|面试", MailCategory.Career, Importance.P1, 3, 100, true, RuleSource.Builtin),
        new ClassifyRule(Guid.NewGuid(), "P0-Deadline", RuleKind.SubjectRegex, "(final reminder|overdue)", null, Importance.P0, 6, 100, true, RuleSource.Builtin),
    }));

    private async Task<MailRepository> SeedAsync(params MailMessage[] messages)
    {
        var repo = new MailRepository(_dbPath);
        if (messages.Length > 0)
        {
            await repo.UpsertRangeAsync("acc-1", messages, CancellationToken.None);
        }

        return repo;
    }

    private static MailMessage Msg(string id, string subject, string preview) => new(
        id, "acc-1", $"<{id}@im>", subject, "Sender", "someone@example.com", preview, null,
        new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc), false, false,
        MailCategory.Other, Importance.P2, null, "rule", null, "ck", false);

    [Fact]
    public async Task ClassifyPending_WritesBackAndSummarizes()
    {
        await SeedAsync(
            Msg("m1", "Assignment 1 released", "coursework details"),
            Msg("m2", "Interview invitation", "we invite you"),
            Msg("m3", "嗨，随便聊聊", "无关键词"));
        var service = new ClassificationService(NewClassifier(), new MailRepository(_dbPath), "acc-1");

        var summary = await service.ClassifyPendingAsync(100, CancellationToken.None);

        summary.Processed.Should().Be(3);
        summary.CategoryCounts[MailCategory.Course].Should().Be(1);
        summary.CategoryCounts[MailCategory.Career].Should().Be(1);
        summary.CategoryCounts[MailCategory.Other].Should().Be(1);
        summary.PendingReview.Should().Be(1); // 仅低置信度的 m3

        var repo = new MailRepository(_dbPath);
        (await repo.GetPendingClassificationAsync("acc-1", 10, CancellationToken.None)).Should().BeEmpty(); // 全部已写回

        var m2 = (await repo.SearchFtsAsync("acc-1", "interview", 10, CancellationToken.None)).Single();
        m2.Category.Should().Be(MailCategory.Career);
        m2.Importance.Should().Be(Importance.P1); // 规则 hint 生效
        m2.Confidence.Should().BeGreaterThan(0.5);
        m2.ClassifiedBy.Should().Be(RuleEngine.EngineName);
    }

    [Fact]
    public async Task ClassifyPending_EmptyBatch_NoWrites()
    {
        await SeedAsync();
        var service = new ClassificationService(NewClassifier(), new MailRepository(_dbPath), "acc-1");

        var summary = await service.ClassifyPendingAsync(100, CancellationToken.None);

        summary.Processed.Should().Be(0);
        summary.CategoryCounts.Should().BeEmpty();
        summary.PendingReview.Should().Be(0);
    }

    [Fact]
    public async Task ClassifyPending_QuotedDeadlineInPreview_DoesNotTriggerP0()
    {
        // R-06 端到端：管线预处理剥引文后才分类
        await SeedAsync(Msg("m1", "Assignment 2 released",
            "Assignment details.\n\nFrom: x@hku.hk\nSent: Mon, 07 Sep 2026 09:00:00 +0000\n> final reminder overdue"));
        var service = new ClassificationService(NewClassifier(), new MailRepository(_dbPath), "acc-1");

        await service.ClassifyPendingAsync(100, CancellationToken.None);

        var repo = new MailRepository(_dbPath);
        var m1 = (await repo.SearchFtsAsync("acc-1", "assignment", 10, CancellationToken.None)).Single();
        m1.Category.Should().Be(MailCategory.Course);
        m1.Importance.Should().NotBe(Importance.P0); // 引文深处的 final reminder 不触发 P0
    }

    [Fact]
    public async Task ClassifyPending_ReviewThreshold_Injectable()
    {
        await SeedAsync(
            Msg("m1", "Assignment 1 released", "coursework"),
            Msg("m2", "Interview invitation", "we invite you"),
            Msg("m3", "嗨", "无关键词"));
        var service = new ClassificationService(NewClassifier(), new MailRepository(_dbPath), "acc-1", reviewThreshold: 1.01);

        var summary = await service.ClassifyPendingAsync(100, CancellationToken.None);

        summary.PendingReview.Should().Be(3); // 极端阈值验证可注入性：含 conf=1.0 的命中在内全部落入待确认
    }
}
