using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace MailHelper.Core.TextProcessing;

/// <summary>预处理（04 章 §2.2 TextNormalizer）：
/// 1. HTML → 纯文本（剔除 script/style）；
/// 2. 剥离引用块（&gt; 前缀行、From:/Sent: 分隔线以下、中文「在…写道：」）与签名（-- 分隔符）；
/// 3. 折叠空白；保留中英文原文（不翻译）。</summary>
public static partial class TextNormalizer
{
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "li", "tr", "table", "blockquote", "h1", "h2", "h3", "h4", "h5", "h6",
        "hr", "pre", "section", "article", "header", "footer", "ul", "ol", "dl", "dt", "dd",
        "address", "figure", "figcaption", "main", "nav", "aside",
    };

    [GeneratedRegex(@"</?[a-z][a-z0-9]*(\s[^<>]*)?/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"^(from|sent)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuoteHeaderEnRegex();

    [GeneratedRegex(@"^在.{1,60}写道[:：]$")]
    private static partial Regex QuoteHeaderZhRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string Normalize(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var text = HtmlTagRegex().IsMatch(content) ? HtmlToText(content) : content;
        return StripQuoteAndSignature(text);
    }

    private static string HtmlToText(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var removable = doc.DocumentNode.SelectNodes("//script|//style");
        if (removable is not null)
        {
            foreach (var node in removable.ToList())
            {
                node.Remove();
            }
        }

        var sb = new StringBuilder(Math.Min(html.Length, 128 * 1024));
        Walk(doc.DocumentNode, sb);
        return sb.ToString();
    }

    private static void Walk(HtmlNode node, StringBuilder sb)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            sb.Append(System.Net.WebUtility.HtmlDecode(((HtmlTextNode)node).Text));
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            Walk(child, sb);
        }

        if (BlockTags.Contains(node.Name))
        {
            sb.Append('\n');
        }
    }

    private static string StripQuoteAndSignature(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new StringBuilder(normalized.Length);

        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                AppendBlankLine(sb);
            }
            else if (line == "--")
            {
                break; // 签名分隔符（04 §2.2 启发式）
            }
            else if (line[0] == '>')
            {
                break; // 引用块起点：> 前缀行
            }
            else if (string.Equals(line, "-----Original Message-----", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            else if (QuoteHeaderEnRegex().IsMatch(line) || QuoteHeaderZhRegex().IsMatch(line))
            {
                break; // From:/Sent: 分隔线以下、中文「在…写道：」以下视为引用
            }
            else
            {
                AppendContentLine(sb, WhitespaceRegex().Replace(line, " ").Trim());
            }
        }

        return sb.ToString().Trim();
    }

    private static void AppendContentLine(StringBuilder sb, string content)
    {
        if (content.Length == 0)
        {
            AppendBlankLine(sb);
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append('\n');
        }

        sb.Append(content);
    }

    private static void AppendBlankLine(StringBuilder sb)
    {
        // 开头空行丢弃；连续空行折叠为单个
        if (sb.Length == 0 || sb[^1] == '\n')
        {
            return;
        }

        sb.Append('\n');
    }
}
