using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.TextProcessing;
using Xunit;

namespace MailHelper.Core.Tests.Rules;

file static class R
{
    public const string DeadlinePattern =
        "(final reminder|overdue|deadline.*(tomorrow|today)|(缴费|递交).{0,6}(截止|逾期))";

    public static ClassifyRule Make(
        string name, RuleKind kind, string pattern, MailCategory? category,
        Importance? hint = null, double? weight = null, int priority = 100,
        bool enabled = true, RuleSource source = RuleSource.Builtin, Guid? id = null)
        => new(id ?? Guid.NewGuid(), name, kind, pattern, category, hint,
               weight ?? RuleSet.DefaultWeight(kind), priority, enabled, source);

    public static RuleSet Set(params ClassifyRule[] rules) => new("test", new RuleScoring(), rules);

    public static ClassifiedInput Input(string subject, string from = "someone@hku.hk", string? body = null)
        => new(subject, "Sender", from, body, ReceivedAtUtc: null);
}

/// <summary>RuleEngine 加权评分与双阈值判定（03 章 §5.3；06 章 §4.3 边界样例 R-01 ~ R-07）。</summary>
public class RuleEngineTests
{
    // ———— R-01 ~ R-07（06 章 §4.3）————

    [Fact]
    public async Task R01_FinalReminderTuition_IsFinanceP0()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Finance-Billing", RuleKind.SubjectKeyword, "tuition|payment due|invoice|缴费", MailCategory.Finance, hint: Importance.P1, weight: 6),
            R.Make("P0-Deadline", RuleKind.SubjectRegex, R.DeadlinePattern, null, hint: Importance.P0, weight: 6)));

        var result = await engine.ClassifyAsync(R.Input("FINAL REMINDER: Tuition Fee Payment", body: "Your tuition is due."), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Finance);
        result.Importance.Should().Be(Importance.P0);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.6);
        result.ClassifiedBy.Should().Be(RuleEngine.EngineName);
    }

    [Fact]
    public async Task R02_InterviewInvitation_IsCareerP1()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Career-Invitation", RuleKind.SubjectKeyword, "interview|面试邀约|面试邀请", MailCategory.Career, hint: Importance.P1)));

        var result = await engine.ClassifyAsync(R.Input("面试邀约 Interview Invitation - Summer Intern"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Career);
        result.Importance.Should().Be(Importance.P1);
    }

    [Fact]
    public async Task R03_MoodleCourseware_IsCourseP2()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Moodle-HKU", RuleKind.SenderDomain, "moodle.hku.hk", MailCategory.Course)));

        var result = await engine.ClassifyAsync(
            R.Input("课件更新 CS101 Week 3 slides", from: "noreply@moodle.hku.hk"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Course);
        result.Importance.Should().Be(Importance.P2); // 无重要度线索 → 默认 P2（02 章附录 A）
        result.Confidence.Should().BeApproximately(0.9, 0.0001); // 发件人锁定
    }

    [Fact]
    public async Task R04_NewsletterWithUnsubscribe_IsSubscriptionP3()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Newsletter-Noise", RuleKind.SubjectKeyword, "newsletter|unsubscribe|promotion", MailCategory.Subscription, hint: Importance.P3)));

        var result = await engine.ClassifyAsync(
            R.Input("Student Newsletter - September", body: "click here to unsubscribe"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Subscription);
        result.Importance.Should().Be(Importance.P3);
    }

    [Fact]
    public async Task R05_UnknownSenderNoKeyword_IsOtherPendingReview()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Moodle-HKU", RuleKind.SenderDomain, "moodle.hku.hk", MailCategory.Course)));

        var result = await engine.ClassifyAsync(
            R.Input("嗨", from: "stranger@nowhere.example", body: "随便聊聊"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Other);      // FR-07 AC2：无命中归「其他」
        result.Confidence.Should().Be(0);                     // 置信度低于阈值 → 待确认（FR-09，下游 S5 判定）
    }

    [Fact]
    public async Task R06_DeadlineOnlyInQuotedBody_DoesNotTriggerP0()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Timetable", RuleKind.SubjectKeyword, "timetable|课程表", MailCategory.Course),
            R.Make("P0-Deadline", RuleKind.SubjectRegex, R.DeadlinePattern, null, hint: Importance.P0, weight: 6)));

        var rawBody = "Your timetable is ready.\n\nFrom: office@hku.hk\nSent: 2026-09-01\n> deadline tomorrow";
        var result = await engine.ClassifyAsync(
            R.Input("Your timetable is ready", body: TextNormalizer.Normalize(rawBody)), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Course);
        result.Importance.Should().NotBe(Importance.P0); // 引文深处的 deadline 不判 P0（预处理剥离验证）
        result.Importance.Should().Be(Importance.P2);
    }

    [Fact]
    public async Task R07_ReDoSProbe_RegexTimeoutSkippedAndDisabled()
    {
        var evil = R.Make("Evil-Regex", RuleKind.SubjectRegex, "(a+)+$", null, weight: 6);
        var engine = new RuleEngine(R.Set(
            R.Make("Coursework", RuleKind.SubjectKeyword, "assignment|作业", MailCategory.Course),
            evil));

        var subject = new string('a', 60) + "!";
        var act = () => engine.ClassifyAsync(R.Input(subject), CancellationToken.None);
        var result = await act(); // 不得抛异常、不卡死（CLASS-001）

        result.Category.Should().Be(MailCategory.Other);
        engine.TemporarilyDisabledRuleIds.Should().Contain(evil.Id);
    }

    // ———— 匹配行为 ————

    [Fact]
    public async Task SenderAddress_LocksCategory()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Dean-Feedback", RuleKind.SenderAddress, "dean@ust.hk", MailCategory.Admin, weight: 9, source: RuleSource.Feedback)));

        var result = await engine.ClassifyAsync(R.Input("任何主题", from: "dean@ust.hk"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Admin);
        result.Confidence.Should().BeApproximately(0.9, 0.0001);
    }

    [Fact]
    public async Task SenderAddress_IsCaseInsensitive()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Dean", RuleKind.SenderAddress, "Dean@UST.HK", MailCategory.Admin)));

        var result = await engine.ClassifyAsync(R.Input("x", from: "dean@ust.hk"), CancellationToken.None);
        result.Category.Should().Be(MailCategory.Admin);
    }

    [Fact]
    public async Task SenderDomain_OtherDomainDoesNotLock()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("UST", RuleKind.SenderDomain, "ust.hk", MailCategory.Admin)));

        var result = await engine.ClassifyAsync(R.Input("x", from: "someone@cuhk.edu.hk"), CancellationToken.None);
        result.Category.Should().Be(MailCategory.Other);
        result.Confidence.Should().Be(0);
    }

    [Fact]
    public async Task SubjectKeyword_MatchesPreprocessedBodyToo()
    {
        // 03 章 §5.3：主题+正文关键词加权评分（D-11）
        var engine = new RuleEngine(R.Set(
            R.Make("Career", RuleKind.SubjectKeyword, "placement", MailCategory.Career)));

        var result = await engine.ClassifyAsync(R.Input("Hello there", body: "Placement season opens"), CancellationToken.None);
        result.Category.Should().Be(MailCategory.Career);
    }

    [Fact]
    public async Task SubjectRegex_IsCaseInsensitive()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Inv", RuleKind.SubjectRegex, "INVITATION", MailCategory.Career)));

        var result = await engine.ClassifyAsync(R.Input("You have an invitation"), CancellationToken.None);
        result.Category.Should().Be(MailCategory.Career);
    }

    [Fact]
    public async Task DisabledRule_IsIgnored()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Course", RuleKind.SubjectKeyword, "cs", MailCategory.Course, enabled: false)));

        var result = await engine.ClassifyAsync(R.Input("cs syllabus"), CancellationToken.None);
        result.Category.Should().Be(MailCategory.Other);
    }

    [Fact]
    public async Task Priority_ResolvesConflictingSenderLocks()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Dean-Admin", RuleKind.SenderAddress, "dean@ust.hk", MailCategory.Admin, weight: 9, priority: 1),
            R.Make("Dean-Course", RuleKind.SenderAddress, "dean@ust.hk", MailCategory.Course, weight: 10, priority: 2)));

        var result = await engine.ClassifyAsync(R.Input("x", from: "dean@ust.hk"), CancellationToken.None);
        result.Category.Should().Be(MailCategory.Admin); // priority 小者先匹配
    }

    // ———— 双阈值与置信度 ————

    [Fact]
    public async Task Threshold_Top1BelowMin_RejectedToOther()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Course", RuleKind.SubjectKeyword, "cs", MailCategory.Course, weight: 2.9)));

        var result = await engine.ClassifyAsync(R.Input("cs syllabus"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Other);
        result.Confidence.Should().BeApproximately(0.5, 0.0001); // 有命中但不过阈值 → 待确认区间
    }

    [Fact]
    public async Task Threshold_GapExactlyTwo_IsAccepted()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Course", RuleKind.SubjectKeyword, "cs", MailCategory.Course, weight: 3),
            R.Make("Career", RuleKind.SubjectKeyword, "talk", MailCategory.Career, weight: 1)));

        var result = await engine.ClassifyAsync(R.Input("cs talk"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Course);
        result.Confidence.Should().BeApproximately(0.75, 0.0001); // 3/(3+1)
    }

    [Fact]
    public async Task Threshold_GapBelowTwo_RejectedToOther()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Course", RuleKind.SubjectKeyword, "cs", MailCategory.Course, weight: 3.5),
            R.Make("Career", RuleKind.SubjectKeyword, "talk", MailCategory.Career, weight: 4.5)));

        var result = await engine.ClassifyAsync(R.Input("cs talk"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Other);
        result.Confidence.Should().BeLessThan(0.4); // 模糊区间 → 待确认
    }

    [Fact]
    public async Task UserRuleMultiplier_BoostsWeight()
    {
        // 2.9 × 1.2 = 3.48 ≥ 3 过阈值；不乘倍率则 2.9 < 3 应拒绝
        var engine = new RuleEngine(R.Set(
            R.Make("User-CS", RuleKind.SubjectKeyword, "cs", MailCategory.Course, weight: 2.9, source: RuleSource.User)));

        var result = await engine.ClassifyAsync(R.Input("cs syllabus"), CancellationToken.None);
        result.Category.Should().Be(MailCategory.Course);
    }

    [Fact]
    public async Task FeedbackMultiplier_CappedAtWeightCap()
    {
        // Feedback 20 × 1.5 = 30 → 截断 15；Builtin 13 不乘 → top1=15, top2=13, conf=15/28
        var engine = new RuleEngine(R.Set(
            R.Make("Fin", RuleKind.SubjectKeyword, "pay", MailCategory.Finance, weight: 20, source: RuleSource.Feedback),
            R.Make("Car", RuleKind.SubjectKeyword, "job", MailCategory.Career, weight: 13)));

        var result = await engine.ClassifyAsync(R.Input("pay job"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Finance);
        result.Confidence.Should().BeApproximately(15.0 / 28.0, 0.001);
    }

    // ———— 重要度判定（P0 保守双阈值）————

    [Fact]
    public async Task P0SingleSignal_WithoutStrongCategory_DowngradedToP1()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("P0-Deadline", RuleKind.SubjectRegex, R.DeadlinePattern, null, hint: Importance.P0, weight: 6)));

        var result = await engine.ClassifyAsync(R.Input("FINAL REMINDER: act now"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Other);
        result.Importance.Should().Be(Importance.P1); // 宁漏报为 P1，不滥标 P0（02 章附录 B）
    }

    [Fact]
    public async Task P0WithSenderLock_IsPromoted()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("HKU-Finance", RuleKind.SenderDomain, "hku.hk", MailCategory.Finance, hint: Importance.P1),
            R.Make("P0-Deadline", RuleKind.SubjectRegex, R.DeadlinePattern, null, hint: Importance.P0, weight: 6)));

        var result = await engine.ClassifyAsync(
            R.Input("Tuition deadline today", from: "finance@hku.hk"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Finance);
        result.Importance.Should().Be(Importance.P0); // P0 线索 + 强类别证据（双信号）
    }

    [Fact]
    public async Task P0DeadlineOnNonActionCategory_IsNotPromoted()
    {
        // D-41：订阅/公告类别的 final reminder 不构成 P0（P0 误报率红线，06 §4.1）
        var engine = new RuleEngine(R.Set(
            R.Make("Newsletter", RuleKind.SubjectKeyword, "newsletter|subscribe", MailCategory.Subscription, hint: Importance.P3),
            R.Make("P0-Deadline", RuleKind.SubjectRegex, R.DeadlinePattern, null, hint: Importance.P0, weight: 6)));

        var result = await engine.ClassifyAsync(
            R.Input("Final reminder: renew your newsletter subscription", body: "subscribe now"), CancellationToken.None);

        result.Category.Should().Be(MailCategory.Subscription);
        result.Importance.Should().NotBe(Importance.P0);
    }

    [Fact]
    public async Task HintZero_YieldsP3()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("Promo", RuleKind.SubjectKeyword, "promo", MailCategory.Subscription, hint: Importance.P3)));

        var result = await engine.ClassifyAsync(R.Input("promo offer"), CancellationToken.None);
        result.Importance.Should().Be(Importance.P3);
    }

    // ———— 热重载与接口 ————

    [Fact]
    public async Task Swap_ReplacesRuleSet()
    {
        var engine = new RuleEngine(R.Set(
            R.Make("CS", RuleKind.SubjectKeyword, "cs", MailCategory.Course)));
        (await engine.ClassifyAsync(R.Input("cs talk"), CancellationToken.None))
            .Category.Should().Be(MailCategory.Course);

        engine.Swap(R.Set(
            R.Make("CS2", RuleKind.SubjectKeyword, "cs", MailCategory.Career)));

        (await engine.ClassifyAsync(R.Input("cs talk"), CancellationToken.None))
            .Category.Should().Be(MailCategory.Career);
    }

    [Fact]
    public async Task ClassifyAsync_UsableViaIClassifierInterface()
    {
        IClassifier classifier = new RuleEngine(R.Set(
            R.Make("Career", RuleKind.SubjectKeyword, "interview", MailCategory.Career, hint: Importance.P1)));

        classifier.Name.Should().Be("rule-engine");
        var result = await classifier.ClassifyAsync(R.Input("Interview invitation"), CancellationToken.None);
        result.Category.Should().Be(MailCategory.Career);
    }
}
