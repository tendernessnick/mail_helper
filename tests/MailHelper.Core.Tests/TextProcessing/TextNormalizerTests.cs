using FluentAssertions;
using MailHelper.Core.TextProcessing;
using Xunit;

namespace MailHelper.Core.Tests.TextProcessing;

/// <summary>TextNormalizer 预处理（04 章 §2.2；含 R-06 引文剥离验证）。</summary>
public class TextNormalizerTests
{
    [Fact]
    public void NullOrWhitespace_ReturnsEmpty()
    {
        TextNormalizer.Normalize(null).Should().BeEmpty();
        TextNormalizer.Normalize("   \n\t ").Should().BeEmpty();
    }

    [Fact]
    public void PlainText_KeepsZhAndEn_AndCollapsesInlineWhitespace()
    {
        TextNormalizer.Normalize("Hello   世界\t\t邮件").Should().Be("Hello 世界 邮件");
    }

    [Fact]
    public void Html_RemovesScriptAndStyle()
    {
        var html = "<html><body><style>.x{color:red}</style><script>alert('boom')</script><p>成绩 Grade 已发布</p></body></html>";
        var text = TextNormalizer.Normalize(html);
        text.Should().Contain("成绩 Grade 已发布");
        text.Should().NotContain("alert");
        text.Should().NotContain("color");
    }

    [Fact]
    public void Html_BlockTagsProduceLineBreaks()
    {
        var html = "<p>First</p><p>Second</p><div>Third</div>";
        var lines = TextNormalizer.Normalize(html)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToArray();
        lines.Should().HaveCount(3);
        lines[0].Should().Be("First");
        lines[2].Should().Be("Third");
    }

    [Fact]
    public void Html_DecodesEntities()
    {
        TextNormalizer.Normalize("<p>A &amp; B</p>").Should().Be("A & B");
    }

    [Fact]
    public void StripsQuotedLines_WithGreaterThanPrefix()
    {
        TextNormalizer.Normalize("Answer here\n> old message deadline tomorrow").Should().Be("Answer here");
    }

    [Fact]
    public void StripsQuoteBlock_AfterFromSentHeader()
    {
        // R-06 场景：deadline 出现在引用深处，预处理后不得残留
        var body = "Your timetable is ready.\n\nFrom: office@hku.hk\nSent: 2026-09-01\n> deadline tomorrow";
        TextNormalizer.Normalize(body).Should().Be("Your timetable is ready.");
    }

    [Fact]
    public void StripsQuoteBlock_AfterZhHeader()
    {
        var body = "收到，请查收\n在 2026年9月1日 张老师 写道：\n> 引用内容";
        TextNormalizer.Normalize(body).Should().Be("收到，请查收");
    }

    [Fact]
    public void StripsSignature_AfterDoubleDash()
    {
        TextNormalizer.Normalize("正文内容\n--\nDean Office").Should().Be("正文内容");
    }

    [Fact]
    public void CollapsesMultipleBlankLines()
    {
        TextNormalizer.Normalize("A\n\n\n\nB").Should().Be("A\n\nB");
    }

    [Fact]
    public void TrimsLeadingAndTrailingWhitespace()
    {
        TextNormalizer.Normalize("  \n Hello \n ").Should().Be("Hello");
    }
}
