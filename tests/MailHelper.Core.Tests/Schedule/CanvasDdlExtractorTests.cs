using FluentAssertions;
using MailHelper.Core.Schedule;
using Xunit;

namespace MailHelper.Core.Tests.Schedule;

/// <summary>Canvas DDL 解析器（S18/CHG-015）：摘要周报多条并列/单封通知/日期格式族/年份滚转/去重键/无 due 过滤。
/// 纯函数零 IO；断言基于墙上时间（Kind=Unspecified），与时区无关。</summary>
public class CanvasDdlExtractorTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 4, 4, 0, 0, DateTimeKind.Utc);

    private readonly CanvasDdlExtractor _extractor = new();

    /// <summary>截图样式的 Canvas 周报摘要（3 条并列）。</summary>
    private const string DigestText = """
        You're signed up to receive a weekly report of some notifications from your Canvas account. Below is the report for the week ending Oct 3:

        Assignment Created - Week 5 assignment, IS6400 Business Data Analytics
        due: Oct 9 at 11:59pm
        Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/187001>

        Assignment Created - Week 5 Monday In-class Quiz, IS6400 Business Data Analytics
        due: Sep 28 at 5pm
        Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/187002>

        Assignment Created - Week 5 Exercise Submission Box, IS6335 Data Visualization
        due: Oct 4 at 11:59pm
        Click to view <https://canvas.cityu.edu.hk/courses/72000/assignments/188000>
        """;

    [Fact]
    public void Digest_MultipleItems_AllParsed()
    {
        var candidates = _extractor.Parse(DigestText, NowUtc);

        candidates.Should().HaveCount(3);

        var first = candidates[0];
        first.Title.Should().Be("Week 5 assignment");
        first.Course.Should().Be("IS6400 Business Data Analytics");
        first.CourseCode.Should().Be("IS6400");
        first.Link.Should().Be("https://canvas.cityu.edu.hk/courses/71457/assignments/187001");
        first.DedupeKey.Should().Be("canvas-a:71457:187001"); // D-70：链接复合 id
        first.DueLocal.Should().Be(new DateTime(2026, 10, 9, 23, 59, 0));

        candidates[1].Title.Should().Be("Week 5 Monday In-class Quiz");
        candidates[1].DueLocal.Should().Be(new DateTime(2026, 9, 28, 17, 0, 0)); // "5pm"
        candidates[2].Course.Should().Be("IS6335 Data Visualization");
        candidates[2].DueLocal.Should().Be(new DateTime(2026, 10, 4, 23, 59, 0));
    }

    [Fact]
    public void SingleMail_WithBlankLines_Parsed()
    {
        const string body = """
            Assignment Created - Week 4 assignment, IS6400 Business Data Analytics

            due: Oct 2 at 11:59pm

            Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/186500>

            You can view and submit this assignment in Canvas.
            """;

        var candidates = _extractor.Parse(body, NowUtc);

        candidates.Should().HaveCount(1);
        candidates[0].Title.Should().Be("Week 4 assignment");
        candidates[0].DueLocal.Should().Be(new DateTime(2026, 10, 2, 23, 59, 0));
    }

    [Theory]
    [InlineData("Oct 9 at 11:59pm", 10, 9, 23, 59)]
    [InlineData("October 9 at 11:59 PM", 10, 9, 23, 59)] // 全月名 + 大写 PM
    [InlineData("Sep 28 at 5pm", 9, 28, 17, 0)]
    [InlineData("Oct 9", 10, 9, 23, 59)] // 无时间默认 23:59
    [InlineData("Oct 9 at noon", 10, 9, 12, 0)]
    [InlineData("Oct 9 at midnight", 10, 9, 0, 0)]
    [InlineData("Oct 9, 2026 at 9:00am", 10, 9, 9, 0)] // 显式年份
    [InlineData("10/9 at 19:00", 10, 9, 19, 0)] // 数字式（港校惯用）
    [InlineData("10月9日 23:59", 10, 9, 23, 59)] // 中文式
    public void ParseDueText_SupportedFormats(string raw, int month, int day, int hour, int minute)
    {
        var due = _extractor.ParseDueText(raw, NowUtc);

        due.Should().Be(new DateTime(2026, month, day, hour, minute, 0));
    }

    [Fact]
    public void ParseDueText_YearRollover_AcrossAcademicYear()
    {
        // 无年份且早于 now−45 天（2 月 10 日在 10 月初看来属「下学年」）→ 滚 +1 年（ADR-006）
        var due = _extractor.ParseDueText("Feb 10 at 9:00am", NowUtc);

        due.Should().Be(new DateTime(2027, 2, 10, 9, 0, 0));
    }

    [Fact]
    public void ParseDueText_RecentPast_KeepsCurrentYear()
    {
        // 摘要常含刚截止的作业（9 月 28 日距 10 月 4 日 < 45 天）：保持 2026，不滚年
        var due = _extractor.ParseDueText("Sep 28 at 5pm", NowUtc);

        due.Should().Be(new DateTime(2026, 9, 28, 17, 0, 0));
    }

    [Theory]
    [InlineData("somewhere soon")]
    [InlineData("第 10 周")]
    [InlineData("")]
    public void ParseDueText_UnsupportedFormats_ReturnNull(string raw)
    {
        _extractor.ParseDueText(raw, NowUtc).Should().BeNull();
    }

    [Fact]
    public void Parse_NoDueLine_NoCandidate() // 成绩/讨论类通知自然过滤（AC5）
    {
        const string body = """
            Assignment Created - Group Project Briefing Slides, IS6400 Business Data Analytics
            Slides are now available. Please review before the briefing.
            """;

        _extractor.Parse(body, NowUtc).Should().BeEmpty();
    }

    [Fact]
    public void Parse_UnrelatedBody_Empty()
    {
        const string body = "Your weekly Canvas report is ready. Manage your notification preferences.";
        _extractor.Parse(body, NowUtc).Should().BeEmpty();
        _extractor.Parse(null, NowUtc).Should().BeEmpty();
        _extractor.Parse(string.Empty, NowUtc).Should().BeEmpty();
    }

    [Fact]
    public void Parse_DuplicateAssignmentInDigest_SingleCandidate()
    {
        const string body = """
            Assignment Created - Week 5 assignment, IS6400 Business Data Analytics
            due: Oct 9 at 11:59pm
            Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/187001>

            Assignment Created - Week 5 assignment, IS6400 Business Data Analytics
            due: Oct 9 at 11:59pm
            Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/187001>
            """;

        _extractor.Parse(body, NowUtc).Should().HaveCount(1); // 批内去重键（AC3）
    }

    [Fact]
    public void Parse_DueDateChangedVariant_Parsed()
    {
        const string body = """
            Assignment Due Date Changed - Week 6 assignment, IS6400 Business Data Analytics
            due: Oct 16 at 11:59pm
            Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/187100>
            """;

        var candidates = _extractor.Parse(body, NowUtc);

        candidates.Should().HaveCount(1);
        candidates[0].DueLocal.Should().Be(new DateTime(2026, 10, 16, 23, 59, 0));
        candidates[0].DedupeKey.Should().Be("canvas-a:71457:187100");
    }

    [Fact]
    public void Parse_KeywordAloneOnLine_NextLineAsTitle()
    {
        const string body = """
            Assignment Created
            Week 6 assignment, IS6400 Business Data Analytics
            due: Oct 16 at 11:59pm
            """;

        var candidates = _extractor.Parse(body, NowUtc);

        candidates.Should().HaveCount(1);
        candidates[0].Title.Should().Be("Week 6 assignment");
    }

    [Fact]
    public void Parse_NoLink_FallsBackToHashKey()
    {
        const string body = """
            Quiz Created - Week 5 Quiz, IS6400 Business Data Analytics
            due: Oct 9 at 11:59pm
            """;

        var candidates = _extractor.Parse(body, NowUtc);

        candidates.Should().HaveCount(1);
        candidates[0].Link.Should().BeNull();
        candidates[0].DedupeKey.Should().StartWith("canvas-t:").And.HaveLength(9 + 16);
    }

    [Fact]
    public void Parse_TitleWithoutComma_ExtractsCourseCodeOnly()
    {
        const string body = """
            Assignment Created - IS6400 Week 7 reflection
            due: Oct 20 at 5pm
            """;

        var candidates = _extractor.Parse(body, NowUtc);

        candidates.Should().HaveCount(1);
        candidates[0].Course.Should().BeNull();
        candidates[0].CourseCode.Should().Be("IS6400");
        candidates[0].Title.Should().Contain("Week 7 reflection");
    }

    [Fact]
    public void Parse_Over50Candidates_Capped()
    {
        var body = string.Join("\n\n", Enumerable.Range(1, 60).Select(i =>
            $"Assignment Created - Week {i} assignment, IS6400 Business Data Analytics\n"
            + $"due: Oct {1 + i % 27} at 11:59pm\n"
            + $"Click to view <https://canvas.cityu.edu.hk/courses/71457/assignments/{100000 + i}>"));

        _extractor.Parse(body, NowUtc).Should().HaveCount(CanvasDdlExtractor.MaxCandidatesPerMail);
    }

    [Fact]
    public void BuildDedupeKey_SameLinkDifferentTitles_SameKey()
    {
        var a = CanvasDdlExtractor.BuildDedupeKey(
            "https://canvas.cityu.edu.hk/courses/71457/assignments/187001", "旧标题", null, new DateTime(2026, 10, 9));
        var b = CanvasDdlExtractor.BuildDedupeKey(
            "https://canvas.cityu.edu.hk/courses/71457/assignments/187001", "新标题", "IS6400", new DateTime(2026, 11, 1));

        a.Should().Be(b); // 同一作业（同链接）跨邮件稳定去重（AC3）
        a.Should().Be("canvas-a:71457:187001");
    }
}
