using FluentAssertions;
using HtmlAgilityPack;
using MailHelper.Infrastructure.Reading;
using Xunit;

namespace MailHelper.Services.Tests;

public sealed class ReaderDocumentTests
{
    [Theory]
    [InlineData(false, "#FFFFFF", "#202B2B")]
    [InlineData(true, "#202D26", "#EEF3EB")]
    public void ForcedSenderColors_AreRemoved_WhileContentAndStructureRemain(bool dark, string paper, string ink)
    {
        const string original = "<html><head><style>p{color:black!important;background:white!important}</style></head>" +
            "<body bgcolor='white' text='black'><table bgcolor='#fff'><tr><td style='color:#000!important;background:#fff!important'>" +
            "<font color='black'><strong>Assignment</strong> deadline</font></td></tr></table>" +
            "<a href='https://example.test/assignment'>Open assignment</a></body></html>";
        var result = ReaderDocument.Build(original, dark);
        var document = new HtmlDocument();
        document.LoadHtml(result);
        var body = document.DocumentNode.SelectSingleNode("//body");
        body.InnerText.Should().Contain("Assignment deadline").And.Contain("Open assignment");
        body.SelectSingleNode(".//strong")!.InnerText.Should().Be("Assignment");
        body.SelectSingleNode(".//a")!.GetAttributeValue("href", "").Should().Be("https://example.test/assignment");
        body.SelectNodes(".//*[@style or @color or @bgcolor or @text]").Should().BeNull();
        result.Should().Contain(paper).And.Contain(ink).And.NotContain("color:black");
    }

    [Fact]
    public void FragmentsAndQuotedText_KeepText_IncludingCodeExamples()
    {
        var result = ReaderDocument.Build("<p>Use &lt;script&gt; as text &amp; keep content.</p><blockquote>Quoted text</blockquote>", true);
        result.Should().Contain("&lt;script&gt;").And.Contain("&amp;").And.Contain("Quoted text");
        var document = new HtmlDocument();
        document.LoadHtml(result);
        document.DocumentNode.SelectSingleNode("//blockquote").Should().NotBeNull();
    }

    [Fact]
    public void ActiveElementsAndInlineHandlers_AreNotCarriedIntoReadingCopy()
    {
        var result = ReaderDocument.Build("<body><script>bad()</script><iframe src='file:///secret'></iframe>" +
            "<p onclick='bad()' style='opacity:0;color:black'>Readable</p></body>", true);
        result.Should().Contain("Readable").And.NotContain("bad()").And.NotContain("onclick").And.NotContain("iframe").And.NotContain("opacity");
    }
}
