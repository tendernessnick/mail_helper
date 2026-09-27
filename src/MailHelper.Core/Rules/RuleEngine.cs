using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Core.Rules;

/// <summary>规则引擎（04 章 §2.3）：实现 IClassifier。
/// 评分模型（03 章 §5.3）：score(category)=Σ 命中规则权重；发件人规则强命中 → 类别锁定；
/// 阈值 top1 ≥ 3 且 (top1 − top2) ≥ 2 才接受，否则 other/待确认；P0 保守双阈值（02 章附录 B）。
/// 正则编译失败/匹配超时（CLASS-001）→ 跳过该规则并自动禁用 24h（进程内）。</summary>
public sealed class RuleEngine : IClassifier
{
    public const string EngineName = "rule-engine";
    public static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(100);

    private const double LockedConfidence = 0.9;
    private const double AmbiguousConfidenceFactor = 0.5;
    private static readonly TimeSpan DisableDuration = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<Guid, DateTime> _disabledUntilUtc = new();
    private readonly object _writeGate = new();
    private volatile CompiledRuleSet _compiled;

    public string Name => EngineName;

    public RuleEngine(RuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        _compiled = Compile(ruleSet);
    }

    /// <summary>热重载：规则 JSON 变更后整体原子替换规则集（引用替换原子，读侧无锁）。</summary>
    public void Swap(RuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        lock (_writeGate)
        {
            _compiled = Compile(ruleSet);
        }
    }

    /// <summary>S8 反馈闭环（FR-11）：单条规则幂等更新——同 Id 替换、否则追加，即时生效。
    /// 写侧串行化避免与 Swap/其他 Upsert 竞争丢更新；读侧仍为无锁原子引用。</summary>
    public void Upsert(ClassifyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_writeGate)
        {
            var current = _compiled;
            var rules = current.Ordered.Select(c => c.Rule)
                .Where(r => r.Id != rule.Id)
                .Append(rule)
                .ToList();
            _compiled = Compile(new RuleSet(current.Version, current.Scoring, rules));
        }
    }

    /// <summary>因正则编译失败/匹配超时（CLASS-001）被临时禁用的规则 id。</summary>
    public IReadOnlyList<Guid> TemporarilyDisabledRuleIds =>
        _disabledUntilUtc
            .Where(kv => kv.Value > DateTime.UtcNow)
            .Select(kv => kv.Key)
            .ToArray();

    public Task<ClassificationResult> ClassifyAsync(ClassifiedInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Classify(input));
    }

    private ClassificationResult Classify(ClassifiedInput input)
    {
        var rules = _compiled;
        var subject = input.Subject ?? string.Empty;
        var body = input.BodyText ?? string.Empty;
        // 03 章 §5.3：主题+正文关键词/正则加权评分（D-11）；BodyText 应为 TextNormalizer 预处理后文本
        var haystack = body.Length == 0 ? subject : subject + "\n" + body;

        var hits = new List<CompiledRule>();
        foreach (var rule in rules.Ordered)
        {
            if (Matches(rule, input, haystack, out var timedOut))
            {
                hits.Add(rule);
            }
            else if (timedOut)
            {
                _disabledUntilUtc[rule.Rule.Id] = DateTime.UtcNow + DisableDuration; // CLASS-001：超时禁用 24h
            }
        }

        var category = ResolveCategory(hits, rules.Scoring, out var confidence, out var strongCategory);
        var importance = ResolveImportance(hits, strongCategory, category);
        return new ClassificationResult(category, importance, Math.Round(confidence, 4), EngineName);
    }

    private bool Matches(CompiledRule compiled, ClassifiedInput input, string haystack, out bool timedOut)
    {
        timedOut = false;
        if (!compiled.Rule.Enabled || IsDisabled(compiled.Rule.Id))
        {
            return false;
        }

        switch (compiled.Rule.Kind)
        {
            case RuleKind.SenderAddress:
                return string.Equals(input.FromAddress, compiled.Rule.Pattern, StringComparison.OrdinalIgnoreCase);

            case RuleKind.SenderDomain:
            {
                var address = input.FromAddress;
                var at = address.LastIndexOf('@');
                if (at < 0 || at == address.Length - 1)
                {
                    return false;
                }

                var domain = address[(at + 1)..];
                return string.Equals(domain, compiled.Rule.Pattern, StringComparison.OrdinalIgnoreCase);
            }

            case RuleKind.SubjectKeyword:
            {
                foreach (var keyword in compiled.Keywords)
                {
                    if (keyword.Length > 0 && haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            case RuleKind.SubjectRegex:
            {
                if (compiled.Regex is null)
                {
                    return false; // 编译失败已禁用
                }

                try
                {
                    return compiled.Regex.IsMatch(haystack);
                }
                catch (RegexMatchTimeoutException)
                {
                    timedOut = true;
                    return false;
                }
            }

            default:
                return false;
        }
    }

    private static MailCategory ResolveCategory(
        List<CompiledRule> hits, RuleScoring scoring, out double confidence, out bool strongCategory)
    {
        // 03 章 §5.3：发件人规则命中 → 强命中锁定类别（排序已按 Priority 升序、Weight 降序）
        var senderLock = hits.FirstOrDefault(h =>
            h.Rule.Kind is RuleKind.SenderAddress or RuleKind.SenderDomain && h.Rule.Category is not null);
        if (senderLock is not null)
        {
            confidence = LockedConfidence;
            strongCategory = true;
            return senderLock.Rule.Category!.Value;
        }

        var votes = hits
            .Where(h => h.Rule.Category is not null)
            .GroupBy(h => h.Rule.Category!.Value)
            .Select(g => (Category: g.Key, Score: g.Sum(h => h.EffectiveWeight)))
            .OrderByDescending(v => v.Score)
            .ToList();

        if (votes.Count == 0)
        {
            confidence = 0;
            strongCategory = false;
            return MailCategory.Other; // FR-07 AC2：无命中归「其他」
        }

        var top1 = votes[0].Score;
        var top2 = votes.Count > 1 ? votes[1].Score : 0;

        if (top1 >= scoring.MinTopScore && top1 - top2 >= scoring.MinGap)
        {
            confidence = top2 <= 0 ? 1.0 : top1 / (top1 + top2);
            strongCategory = true;
            return votes[0].Category;
        }

        // 模糊区间 → other + 待确认（FR-09：分差不足，置信度压低供下游判定）
        confidence = AmbiguousConfidenceFactor * (top2 <= 0 ? 1.0 : top1 / (top1 + top2));
        strongCategory = false;
        return MailCategory.Other;
    }

    private static Importance ResolveImportance(List<CompiledRule> hits, bool strongCategory, MailCategory category)
    {
        var hints = hits
            .Select(h => h.Rule.ImportanceHint)
            .Where(h => h is not null)
            .Select(h => h!.Value)
            .ToList();

        var importance = hints.Count == 0 ? Importance.P2 : hints.Max(); // 02 章附录 A：无线索默认 P2

        if (importance == Importance.P0)
        {
            // P0 保守双阈值：P0 线索之外还需「行动型类别」强证据或第二个独立 P0 规则；否则降 P1（宁可漏报为 P1）。
            // 类别证据白名单（D-41）：订阅/公告类别的 final reminder 等措辞不构成 P0 依据（P0 误报率红线）
            var p0Signals = hints.Count(h => h == Importance.P0);
            var strongEvidence = strongCategory && category
                is MailCategory.Finance or MailCategory.Admin or MailCategory.Course;
            if (p0Signals + (strongEvidence ? 1 : 0) < 2)
            {
                importance = Importance.P1;
            }
        }

        return importance;
    }

    private CompiledRuleSet Compile(RuleSet ruleSet)
    {
        var compiled = new List<CompiledRule>(ruleSet.Rules.Count);
        foreach (var rule in ruleSet.Rules)
        {
            Regex? regex = null;
            string[] keywords = Array.Empty<string>();

            if (rule.Kind == RuleKind.SubjectRegex)
            {
                try
                {
                    regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexMatchTimeout);
                }
                catch (ArgumentException)
                {
                    _disabledUntilUtc[rule.Id] = DateTime.UtcNow + DisableDuration; // CLASS-001：编译失败禁用 24h
                }
            }
            else if (rule.Kind == RuleKind.SubjectKeyword)
            {
                keywords = rule.Pattern.Split('|').Select(k => k.Trim()).ToArray();
            }

            var effective = rule.Weight;
            if (rule.Source == RuleSource.User)
            {
                effective *= ruleSet.Scoring.UserMultiplier;
            }
            else if (rule.Source == RuleSource.Feedback)
            {
                effective *= ruleSet.Scoring.FeedbackMultiplier;
            }

            if (effective > ruleSet.Scoring.WeightCap)
            {
                effective = ruleSet.Scoring.WeightCap; // 上限截断（03 §5.3）
            }

            compiled.Add(new CompiledRule(rule, regex, keywords, effective));
        }

        var ordered = compiled
            .OrderBy(r => r.Rule.Priority)
            .ThenByDescending(r => r.EffectiveWeight)
            .ToList();
        return new CompiledRuleSet(ruleSet.Version, ruleSet.Scoring, ordered);
    }

    private bool IsDisabled(Guid id)
    {
        if (!_disabledUntilUtc.TryGetValue(id, out var until))
        {
            return false;
        }

        if (until <= DateTime.UtcNow)
        {
            _disabledUntilUtc.TryRemove(id, out _);
            return false;
        }

        return true;
    }

    private sealed record CompiledRule(ClassifyRule Rule, Regex? Regex, string[] Keywords, double EffectiveWeight);

    private sealed record CompiledRuleSet(string? Version, RuleScoring Scoring, IReadOnlyList<CompiledRule> Ordered);
}
