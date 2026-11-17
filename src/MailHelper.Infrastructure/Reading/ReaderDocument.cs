using HtmlAgilityPack;

namespace MailHelper.Infrastructure.Reading;

/// <summary>阅读副本使用应用配色，清除发送者强制配色；原始缓存不修改。</summary>
public static class ReaderDocument
{
    private static readonly HashSet<string> RemovedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "link", "iframe", "object", "embed", "base", "meta", "form",
        "input", "button", "textarea", "select", "template", "noscript", "svg", "canvas",
    };

    private static readonly HashSet<string> PresentationAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "color", "bgcolor", "background", "text", "link", "vlink", "alink", "face", "class",
    };

    public static string Build(string html, bool dark)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var body = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        foreach (var node in body.Descendants().ToArray())
        {
            if (RemovedElements.Contains(node.Name))
            {
                node.Remove();
                continue;
            }

            foreach (var attribute in node.Attributes.ToArray())
            {
                if (PresentationAttributes.Contains(attribute.Name)
                    || attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    node.Attributes.Remove(attribute);
                }
            }
        }

        var paper = dark ? "#202D26" : "#FFFFFF";
        var ink = dark ? "#EEF3EB" : "#202B2B";
        var muted = dark ? "#C1CBBE" : "#53645A";
        var line = dark ? "#52675A" : "#CDD8CF";
        var accent = dark ? "#9DD8BE" : "#146B5E";
        var wash = dark ? "#2A3C31" : "#EEF3EC";
        return "<!doctype html><html><head><meta charset=\"utf-8\"><style>" +
            $"html,body{{background:{paper};color:{ink};}}" +
            "body{margin:0;padding:14px 26px 32px;box-sizing:border-box;overflow-wrap:anywhere;" +
            "font:15px/1.8 'Segoe UI','Microsoft YaHei',sans-serif;}" +
            "body *{box-sizing:border-box;max-width:100%;color:inherit;}" +
            "p,pre,ul,ol,table,blockquote{margin:0 0 1em;}pre{white-space:pre-wrap;font:inherit;}" +
            "img{max-width:100%;height:auto;}table{border-collapse:collapse;}" +
            $"td,th{{padding:8px 10px;border:1px solid {line};}}th{{background:{wash};text-align:left;}}" +
            $"a,a *{{color:{accent};text-decoration:underline;}}" +
            $"blockquote{{border-left:3px solid {line};margin-left:0;padding-left:16px;color:{muted};}}" +
            $"code,mark{{color:{ink};background:{wash};padding:2px 4px;}}" +
            "h1,h2,h3,h4{line-height:1.4;margin:1em 0 .5em;}" +
            "</style></head><body>" + body.InnerHtml + "</body></html>";
    }
}
