using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailHelper.Core.Domain;

namespace MailHelper.Core.Rules;

/// <summary>规则 JSON 解析（04 章 §7 Schema：version / scoring? / rules[{name,kind,pattern,category,importanceHint?,weight?,priority?,enabled?,source?}]）。
/// 解析失败返回 false + error（规则 JSON 加载失败即回退内置默认，09 章 §2 Tampering 缓解）。</summary>
public static class RuleSetParser
{
    public static bool TryParseFile(string path, out RuleSet? ruleSet, out string? error)
    {
        try
        {
            return TryParse(File.ReadAllText(path), path, out ruleSet, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ruleSet = null;
            error = $"无法读取规则文件 '{path}': {ex.Message}";
            return false;
        }
    }

    public static bool TryParse(string json, string sourceName, out RuleSet? ruleSet, out string? error)
    {
        try
        {
            var options = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            };
            using var doc = JsonDocument.Parse(json, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                ruleSet = null;
                error = $"规则文件 {sourceName} 根节点必须是 JSON 对象";
                return false;
            }

            var version = root.TryGetProperty("version", out var versionEl)
                && versionEl.ValueKind == JsonValueKind.String
                ? versionEl.GetString()
                : null;

            if (ParseScoring(root, sourceName, out var scoring, out var scoringError) is null)
            {
                ruleSet = null;
                error = scoringError;
                return false;
            }

            var rules = new List<ClassifyRule>();
            if (root.TryGetProperty("rules", out var rulesEl))
            {
                if (rulesEl.ValueKind != JsonValueKind.Array)
                {
                    ruleSet = null;
                    error = $"规则文件 {sourceName} 的 rules 必须是数组";
                    return false;
                }

                var index = 0;
                foreach (var element in rulesEl.EnumerateArray())
                {
                    index++;
                    if (!TryParseRule(element, sourceName, index, out var rule, out var ruleError))
                    {
                        ruleSet = null;
                        error = ruleError;
                        return false;
                    }

                    rules.Add(rule!);
                }
            }

            ruleSet = new RuleSet(version, scoring!, rules);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            ruleSet = null;
            error = $"规则文件 {sourceName} JSON 语法错误: {ex.Message}";
            return false;
        }
    }

    private static RuleScoring? ParseScoring(JsonElement root, string sourceName, out RuleScoring? scoring, out string? error)
    {
        scoring = new RuleScoring();
        error = null;
        if (!root.TryGetProperty("scoring", out var element))
        {
            return scoring;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"规则文件 {sourceName} 的 scoring 必须是对象";
            return null;
        }

        if (!TryGetNumber(element, "minTopScore", defaultValue: scoring.MinTopScore, out var minTopScore, out error)
            || !TryGetNumber(element, "minGap", scoring.MinGap, out var minGap, out error)
            || !TryGetNumber(element, "userMultiplier", scoring.UserMultiplier, out var userMultiplier, out error)
            || !TryGetNumber(element, "feedbackMultiplier", scoring.FeedbackMultiplier, out var feedbackMultiplier, out error)
            || !TryGetNumber(element, "weightCap", scoring.WeightCap, out var weightCap, out error))
        {
            return null;
        }

        scoring = new RuleScoring(minTopScore, minGap, userMultiplier, feedbackMultiplier, weightCap);
        return scoring;
    }

    private static bool TryGetNumber(JsonElement parent, string name, double defaultValue, out double value, out string? error)
    {
        value = defaultValue;
        error = null;
        if (!parent.TryGetProperty(name, out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out value))
        {
            error = $"scoring.{name} 必须是数字";
            return false;
        }

        return true;
    }

    private static bool TryParseRule(JsonElement element, string sourceName, int index, out ClassifyRule? rule, out string? error)
    {
        rule = null;
        var where = $"{sourceName} rules[{index - 1}]";
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"{where} 必须是对象";
            return false;
        }

        if (!element.TryGetProperty("name", out var nameEl)
            || nameEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameEl.GetString()))
        {
            error = $"{where} 缺少有效 name";
            return false;
        }

        var name = nameEl.GetString()!;

        if (!element.TryGetProperty("kind", out var kindEl)
            || kindEl.ValueKind != JsonValueKind.String
            || !Enum.TryParse(kindEl.GetString(), ignoreCase: true, out RuleKind kind))
        {
            var kindValue = kindEl.ValueKind == JsonValueKind.String ? kindEl.GetString() : "(缺失)";
            error = $"{where}({name}) kind '{kindValue}' 无效（允许: SenderAddress/SenderDomain/SubjectRegex/SubjectKeyword）";
            return false;
        }

        if (!element.TryGetProperty("pattern", out var patternEl)
            || patternEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(patternEl.GetString()))
        {
            error = $"{where}({name}) 缺少有效 pattern";
            return false;
        }

        var pattern = patternEl.GetString()!;

        // category 允显式 null 或缺省 → 仅重要度线索（CHG-004）
        MailCategory? category = null;
        if (element.TryGetProperty("category", out var categoryEl)
            && categoryEl.ValueKind == JsonValueKind.String
            && categoryEl.GetString() is { Length: > 0 } categoryString)
        {
            if (!Enum.TryParse(categoryString, ignoreCase: true, out MailCategory parsed))
            {
                error = $"{where}({name}) category '{categoryString}' 无效";
                return false;
            }

            category = parsed;
        }

        Importance? importanceHint = null;
        if (element.TryGetProperty("importanceHint", out var hintEl))
        {
            if (hintEl.ValueKind != JsonValueKind.Number
                || !hintEl.TryGetInt32(out var hint)
                || hint is < 0 or > 3)
            {
                error = $"{where}({name}) importanceHint 必须是 0..3 的整数";
                return false;
            }

            importanceHint = (Importance)hint;
        }

        var weight = RuleSet.DefaultWeight(kind);
        if (element.TryGetProperty("weight", out var weightEl))
        {
            if (weightEl.ValueKind != JsonValueKind.Number
                || !weightEl.TryGetDouble(out var parsedWeight)
                || parsedWeight < 0)
            {
                error = $"{where}({name}) weight 必须是非负数字";
                return false;
            }

            weight = parsedWeight;
        }

        var priority = 100;
        if (element.TryGetProperty("priority", out var priorityEl))
        {
            if (priorityEl.ValueKind != JsonValueKind.Number || !priorityEl.TryGetInt32(out var parsedPriority))
            {
                error = $"{where}({name}) priority 必须是整数";
                return false;
            }

            priority = parsedPriority;
        }

        var enabled = true;
        if (element.TryGetProperty("enabled", out var enabledEl))
        {
            if (enabledEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error = $"{where}({name}) enabled 必须是布尔值";
                return false;
            }

            enabled = enabledEl.GetBoolean();
        }

        var source = RuleSource.Builtin;
        if (element.TryGetProperty("source", out var sourceEl))
        {
            if (sourceEl.ValueKind != JsonValueKind.String
                || !Enum.TryParse(sourceEl.GetString(), ignoreCase: true, out RuleSource parsedSource))
            {
                error = $"{where}({name}) source 无效（允许: Builtin/User/Feedback）";
                return false;
            }

            source = parsedSource;
        }

        rule = new ClassifyRule(StableId(name, pattern), name, kind, pattern, category, importanceHint, weight, priority, enabled, source);
        error = null;
        return true;
    }

    /// <summary>由 name|pattern 派生稳定 id：热重载后同规则 id 不变，临时禁用状态得以延续。</summary>
    private static Guid StableId(string name, string pattern)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name + "|" + pattern));
        return new Guid(hash.AsSpan(0, 16));
    }
}
