using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using Xunit;

namespace MailHelper.Core.Tests.Rules;

/// <summary>规则 JSON 解析（04 章 §7 Schema）。</summary>
public class RuleSetParserTests
{
    private const string CanonicalSample = """
        {
          "version": "2026.09",
          "rules": [
            { "name": "Moodle-HKU",      "kind": "SenderDomain",   "pattern": "moodle.hku.hk",        "category": "course",  "weight": 8 },
            { "name": "Canvas-Broadcast","kind": "SenderAddress",  "pattern": "no-reply@canvas.com",  "category": "course",  "weight": 10 },
            { "name": "Careers-Centre",  "kind": "SubjectKeyword", "pattern": "career|careers|placement", "category": "career", "weight": 8 },
            { "name": "Finance-Billing", "kind": "SubjectKeyword", "pattern": "tuition|payment due|invoice|缴费", "category": "finance", "weight": 6, "importanceHint": 1 },
            { "name": "P0-Deadline",     "kind": "SubjectRegex",   "pattern": "(final reminder|overdue|deadline.*(tomorrow|today)|(缴费|递交).{0,6}(截止|逾期))", "category": null, "weight": 6, "importanceHint": 3 },
            { "name": "Newsletter-Noise","kind": "SubjectKeyword", "pattern": "newsletter|unsubscribe|promotion", "category": "subscription", "weight": 3, "importanceHint": 0 }
          ]
        }
        """;

    [Fact]
    public void ParsesCanonicalSample_AllFieldsVerified()
    {
        var ok = RuleSetParser.TryParse(CanonicalSample, "sample", out var ruleSet, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        ruleSet.Should().NotBeNull();
        ruleSet!.Version.Should().Be("2026.09");
        ruleSet.Rules.Should().HaveCount(6);

        var moodle = ruleSet.Rules[0];
        moodle.Name.Should().Be("Moodle-HKU");
        moodle.Kind.Should().Be(RuleKind.SenderDomain);
        moodle.Pattern.Should().Be("moodle.hku.hk");
        moodle.Category.Should().Be(CategoryIds.Course);
        moodle.Weight.Should().Be(8);
        moodle.Source.Should().Be(RuleSource.Builtin);
        moodle.Enabled.Should().BeTrue();
        moodle.Priority.Should().Be(100);
        moodle.Id.Should().NotBe(Guid.Empty);

        ruleSet.Rules[4].Category.Should().BeNull();                          // 仅重要度线索（CHG-004）
        ruleSet.Rules[4].ImportanceHint.Should().Be(Importance.P0);          // hint 3（04 §7 示例）
        ruleSet.Rules[3].ImportanceHint.Should().Be(Importance.P2);          // hint 1（04 §2.1 枚举数值：P3=0..P0=3）
        ruleSet.Rules[5].ImportanceHint.Should().Be(Importance.P3);          // hint 0
    }

    [Fact]
    public void RawSpecSample_WithUndefinedSenderKeywordKind_Fails()
    {
        // CHG-001：04 §7 示例原样的 "SenderKeyword" 不在 RuleKind 枚举中 → 必须解析失败而非静默忽略
        var raw = CanonicalSample.Replace("\"kind\": \"SubjectKeyword\", \"pattern\": \"career",
                                          "\"kind\": \"SenderKeyword\", \"pattern\": \"career");
        var ok = RuleSetParser.TryParse(raw, "raw", out var ruleSet, out var error);

        ok.Should().BeFalse();
        ruleSet.Should().BeNull();
        error.Should().NotBeNullOrEmpty().And.Contain("Careers-Centre");
    }

    [Fact]
    public void MissingWeight_FallsBackToKindDefault()
    {
        const string json = """
            { "version": "t", "rules": [
              { "name": "A", "kind": "SenderAddress", "pattern": "a@b.c", "category": "course" },
              { "name": "B", "kind": "SubjectKeyword", "pattern": "x", "category": "career" }
            ] }
            """;

        RuleSetParser.TryParse(json, "t", out var ruleSet, out _).Should().BeTrue();
        ruleSet!.Rules[0].Weight.Should().Be(10); // 03 §5.3 权重基准
        ruleSet.Rules[1].Weight.Should().Be(3);
    }

    [Fact]
    public void InvalidJson_ReturnsError()
    {
        var ok = RuleSetParser.TryParse("{ not json", "t", out var ruleSet, out var error);

        ok.Should().BeFalse();
        ruleSet.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ImportanceHintOutOfRange_Fails()
    {
        const string json = """
            { "version": "t", "rules": [
              { "name": "A", "kind": "SubjectKeyword", "pattern": "x", "category": "course", "importanceHint": 4 }
            ] }
            """;

        var ok = RuleSetParser.TryParse(json, "t", out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("importanceHint");
    }

    [Fact]
    public void RuleIds_AreStableAcrossParses()
    {
        RuleSetParser.TryParse(CanonicalSample, "s", out var first, out _).Should().BeTrue();
        RuleSetParser.TryParse(CanonicalSample, "s", out var second, out _).Should().BeTrue();

        first!.Rules.Select(r => r.Id).Should().Equal(second!.Rules.Select(r => r.Id));
    }

    [Fact]
    public void Scoring_OverrideAndDefaults()
    {
        const string withOverride = """
            { "version": "t", "scoring": { "minTopScore": 5, "minGap": 1 }, "rules": [] }
            """;
        const string withoutScoring = """
            { "version": "t", "rules": [] }
            """;

        RuleSetParser.TryParse(withOverride, "t", out var overridden, out _).Should().BeTrue();
        overridden!.Scoring.MinTopScore.Should().Be(5);
        overridden.Scoring.MinGap.Should().Be(1);
        overridden.Scoring.UserMultiplier.Should().Be(1.2);
        overridden.Scoring.FeedbackMultiplier.Should().Be(1.5);
        overridden.Scoring.WeightCap.Should().Be(15);

        RuleSetParser.TryParse(withoutScoring, "t", out var defaults, out _).Should().BeTrue();
        defaults!.Scoring.Should().Be(new RuleScoring());
    }

    [Theory]
    [InlineData("""{ "rules": [ { "name": "A", "kind": "SubjectKeyword", "pattern": "x", "category": "sports" } ] }""", "category")]
    [InlineData("""{ "rules": [ { "name": "A", "kind": "SubjectKeyword", "pattern": "x", "priority": "high" } ] }""", "priority")]
    [InlineData("""{ "rules": [ { "name": "A", "kind": "SubjectKeyword", "pattern": "x", "enabled": "yes" } ] }""", "enabled")]
    [InlineData("""{ "rules": [ { "name": "A", "kind": "SubjectKeyword", "pattern": "x", "source": "Magic" } ] }""", "source")]
    [InlineData("""{ "rules": [ { "name": "A", "kind": "SubjectKeyword", "pattern": "x", "weight": -1 } ] }""", "weight")]
    [InlineData("""{ "rules": [ { "name": "A", "kind": "SubjectKeyword", "pattern": "x", "weight": "six" } ] }""", "weight")]
    [InlineData("""{ "rules": [ { "kind": "SubjectKeyword", "pattern": "x" } ] }""", "name")]
    [InlineData("""{ "rules": [ { "name": "  ", "kind": "SubjectKeyword", "pattern": "x" } ] }""", "name")]
    [InlineData("""{ "rules": [ { "name": "A", "kind": "SubjectRegex" } ] }""", "pattern")]
    [InlineData("""{ "rules": [ { "name": "A", "pattern": "x" } ] }""", "kind")]
    [InlineData("""{ "rules": 42 }""", "rules")]
    [InlineData("""{ "rules": [ 42 ] }""", "必须是对象")]
    [InlineData("""[1, 2]""", "根节点")]
    [InlineData("""{ "scoring": 5, "rules": [] }""", "scoring")]
    [InlineData("""{ "scoring": { "minTopScore": "x" }, "rules": [] }""", "minTopScore")]
    public void MalformedFragments_FailWithTargetedError(string json, string fragment)
    {
        var ok = RuleSetParser.TryParse(json, "t", out var ruleSet, out var error);

        ok.Should().BeFalse();
        ruleSet.Should().BeNull();
        error.Should().NotBeNullOrEmpty().And.Contain(fragment);
    }

    [Fact]
    public void ExplicitNullCategory_AndMissingVersion_Parse()
    {
        const string json = """
            { "rules": [ { "name": "A", "kind": "SubjectKeyword", "pattern": "x", "category": null } ] }
            """;

        RuleSetParser.TryParse(json, "t", out var ruleSet, out _).Should().BeTrue();
        ruleSet!.Version.Should().BeNull();
        ruleSet.Rules.Should().ContainSingle(r => r.Name == "A" && r.Category == null);
    }

    [Fact]
    public void TryParseFile_MissingFile_FailsWithError()
    {
        var path = Path.Combine(Path.GetTempPath(), "mh-no-such-" + Guid.NewGuid().ToString("N") + ".json");

        var ok = RuleSetParser.TryParseFile(path, out var ruleSet, out var error);

        ok.Should().BeFalse();
        ruleSet.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryParseFile_ValidFile_Parses()
    {
        var path = Path.Combine(Path.GetTempPath(), "mh-rules-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, CanonicalSample);

            RuleSetParser.TryParseFile(path, out var ruleSet, out var error).Should().BeTrue();
            error.Should().BeNull();
            ruleSet!.Rules.Should().HaveCount(6);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
