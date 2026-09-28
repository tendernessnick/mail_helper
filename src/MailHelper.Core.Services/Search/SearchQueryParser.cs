using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Core.Services;

/// <summary>搜索语法解析（总控 S10：from:/cat:/p:）。空格分词；前缀大小写不敏感；
/// 非法前缀值整 token 留作自由文本（用户直觉：当作普通词搜）；重复前缀首个生效、后续整 token 归自由文本。</summary>
public static class SearchQueryParser
{
    public static SearchQuery Parse(string input)
    {
        var freeText = new List<string>();
        string? from = null;
        MailCategory? category = null;
        Importance? importance = null;

        foreach (var token in (input ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var (prefix, value) = SplitPrefix(token);
            switch (prefix)
            {
                case "from" when from is null && value.Length > 0:
                    from = value;
                    break;
                case "cat" when category is null && TryParseCategory(value, out var parsed):
                    category = parsed;
                    break;
                case "p" when importance is null && TryParseImportance(value, out var imp):
                    importance = imp;
                    break;
                default:
                    freeText.Add(token); // 非前缀/非法值/重复前缀：按普通词检索
                    break;
            }
        }

        return new SearchQuery(string.Join(' ', freeText), from, category, importance);
    }

    private static (string Prefix, string Value) SplitPrefix(string token)
    {
        var colon = token.IndexOf(':');
        return colon <= 0 || colon == token.Length - 1
            ? (string.Empty, string.Empty)
            : (token[..colon].ToLowerInvariant(), token[(colon + 1)..]);
    }

    private static bool TryParseCategory(string value, out MailCategory category) =>
        Enum.TryParse(value, ignoreCase: true, out category)
        && Enum.IsDefined(typeof(MailCategory), category);

    private static bool TryParseImportance(string value, out Importance importance)
    {
        importance = default;
        if (value.StartsWith("p", StringComparison.OrdinalIgnoreCase))
        {
            value = value[1..];
        }

        if (!int.TryParse(value, out var n) || n is < 0 or > 3)
        {
            return false;
        }

        importance = (Importance)(3 - n); // p:N → 枚举值（P0=3…P3=0，02 章附录 B）
        return Enum.IsDefined(importance);
    }
}
