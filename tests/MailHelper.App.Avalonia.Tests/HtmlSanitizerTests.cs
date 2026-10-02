using FluentAssertions;
using MailHelper.Infrastructure.Reading;
using Xunit;

namespace MailHelper.App.Avalonia.Tests;

/// <summary>净化器白名单行为（docs/10 §5.1 安全结论：无脚本/无远程图片/仅 http(s) 链接）。</summary>
public class HtmlSanitizerTests
{
    [Fact]
    public void FromHtml_StripsScriptStyleImgAndKeepsBlocks()
    {
        var html = """
            <html><head><style>p{color:red}</style><script>alert(1)</script></head>
            <body>
              <h2>学费通知</h2>
              <p>请在 9 月 30 日前缴清。</p>
              <ul><li>第一学期</li><li>第二学期</li></ul>
              <img src="https://tracker.example.com/pixel.png">
              <script>evil()</script>
            </body></html>
            """;

        var blocks = HtmlSanitizer.FromHtml(html);

        blocks.Should().Contain(b => b.Kind == "heading" && b.Text.Contains("学费通知"));
        blocks.Should().Contain(b => b.Kind == "paragraph" && b.Text.Contains("缴清"));
        blocks.Should().Contain(b => b.Kind == "listitem" && b.Text.Contains("第一学期"));
        blocks.Should().NotContain(b => b.Text.Contains("alert") || b.Text.Contains("evil"), "脚本整体剔除");
        blocks.Should().NotContain(b => b.Text.Contains("pixel"), "远程图片不渲染（隐私红线）");
    }

    [Fact]
    public void FromHtml_Link_OnlyHttpHttps_AsLinkBlock()
    {
        var blocks = HtmlSanitizer.FromHtml("""<p><a href="https://example.com/pay">缴费入口</a></p><a href="javascript:alert(1)">x</a>""");

        blocks.OfType<SanitizedLinkBlock>().Should().ContainSingle()
            .Which.Should().Match<SanitizedLinkBlock>(l => l.Href == "https://example.com/pay" && l.Text == "缴费入口");
    }

    [Fact]
    public void FromPlainText_SplitsParagraphs()
    {
        var blocks = HtmlSanitizer.FromPlainText("第一行\r\n第二行\n\n第三行");
        blocks.Should().HaveCount(3);
        blocks[0].Text.Should().Be("第一行");
    }

    [Fact]
    public void FromHtml_NullOrEmpty_ReturnsEmpty()
    {
        HtmlSanitizer.FromHtml(null).Should().BeEmpty();
        HtmlSanitizer.FromHtml("").Should().BeEmpty();
    }
}
