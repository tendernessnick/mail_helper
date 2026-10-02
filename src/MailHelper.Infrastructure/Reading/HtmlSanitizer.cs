using HtmlAgilityPack;

namespace MailHelper.Infrastructure.Reading;

/// <summary>净化后的正文块（docs/10 §5.1 v1 结论：结构化文本渲染——无脚本/无远程图片/无样式执行面）。</summary>
public record SanitizedBlock(string Kind, string Text, string? Href = null) // Kind: heading|paragraph|listitem|quote|link
{
    public static SanitizedBlock Paragraph(string text) => new SanitizedTextBlock("paragraph", text);
}

/// <summary>文本块（paragraph/heading/listitem/quote；无标签体渲染为文本）。</summary>
public sealed record SanitizedTextBlock(string BlockKind, string Text) : SanitizedBlock(BlockKind, Text);

/// <summary>链接块（渲染为可点击链接；仅 http/https）。</summary>
public sealed record SanitizedLinkBlock(string Text, string Href) : SanitizedBlock("link", Text, Href);

/// <summary>HTML → 结构化块（白名单提取；TextNormalizer 同族，供 Avalonia 阅读窗格消费；纯托管全平台）。
/// 规则（docs/10 §5.1）：script/style/iframe/img/noscript 整体剔除；h1-h4→heading；p/div/section/article→paragraph；
/// ul/ol→listitem；blockquote→quote；内联 a→独立 link 块（仅 http/https）；全部属性/样式/事件丢弃。</summary>
public static class HtmlSanitizer
{
    public static IReadOnlyList<SanitizedBlock> FromHtml(string? html)
    {
        var blocks = new List<SanitizedBlock>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return blocks;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        Walk(root, blocks);
        return blocks.Where(b => !string.IsNullOrWhiteSpace(b.Text) || b.Kind == "link").ToList();
    }

    public static IReadOnlyList<SanitizedBlock> FromPlainText(string? text)
    {
        var blocks = new List<SanitizedBlock>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return blocks;
        }

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                blocks.Add(new SanitizedTextBlock("paragraph", trimmed));
            }
        }

        return blocks;
    }

    private static void Walk(HtmlNode node, List<SanitizedBlock> blocks)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child.Name.ToLowerInvariant())
            {
                case "script" or "style" or "iframe" or "img" or "noscript" or "head" or "form" or "input":
                    continue; // 剔除（img=远程图片不加载，隐私红线）
                case "h1" or "h2" or "h3" or "h4":
                    Add(blocks, "heading", child.InnerText);
                    break;
                case "p" or "div" or "section" or "article" or "span" or "tbody" or "table" or "tr" or "td" or "th":
                    // 块级容器整体提取（InnerText 汇总）+ 内部链接一次收集；不再递归（避免链接 double-add）
                    AddWithLinks(blocks, child);
                    break;
                case "ul" or "ol":
                    foreach (var li in child.ChildNodes.Where(n => n.Name.Equals("li", StringComparison.OrdinalIgnoreCase)))
                    {
                        Add(blocks, "listitem", li.InnerText);
                    }

                    break;
                case "blockquote":
                    Add(blocks, "quote", child.InnerText);
                    break;
                case "a":
                    AddLink(blocks, child.GetAttributeValue("href", string.Empty), child.InnerText);
                    break;
                case "br" or "hr":
                    continue;
                default:
                    Walk(child, blocks);
                    break;
            }
        }
    }

    private static void AddWithLinks(List<SanitizedBlock> blocks, HtmlNode node)
    {
        var text = node.InnerText?.Trim() ?? string.Empty;
        if (text.Length > 0)
        {
            Add(blocks, "paragraph", text);
        }

        foreach (var a in node.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = a.GetAttributeValue("href", string.Empty);
            var linkText = a.InnerText?.Trim() ?? string.Empty;
            if ((href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                && linkText.Length > 0)
            {
                AddLink(blocks, href, linkText);
            }
        }
    }

    private static void AddLink(List<SanitizedBlock> blocks, string href, string? text)
    {
        var linkText = (text ?? string.Empty).Trim();
        if ((href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            && linkText.Length > 0)
        {
            blocks.Add(new SanitizedLinkBlock(linkText, href));
        }
    }

    private static void Add(List<SanitizedBlock> blocks, string kind, string? text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r", " ").Replace('\n', ' ').Replace('\t', ' ').Trim();
        if (normalized.Length > 0)
        {
            blocks.Add(new SanitizedTextBlock(kind, normalized));
        }
    }
}
