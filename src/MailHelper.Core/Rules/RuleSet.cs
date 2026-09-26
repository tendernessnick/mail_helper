namespace MailHelper.Core.Rules;

/// <summary>评分参数（03 章 §5.3：权重与阈值集中在外置 JSON，可独立于代码调参——NFR-10）。</summary>
public sealed record RuleScoring(
    double MinTopScore = 3,        // 阈值：top1 ≥ 3
    double MinGap = 2,             // 且 (top1 − top2) ≥ 2，否则 other/待确认
    double UserMultiplier = 1.2,   // 启用中的用户自定义规则倍率
    double FeedbackMultiplier = 1.5, // 用户反馈生成规则倍率
    double WeightCap = 15);        // 倍率上限截断（基准最高 10 × 1.5）

/// <summary>不可变规则集（JSON 加载产物；热重载时整体原子替换）。</summary>
public sealed record RuleSet(string? Version, RuleScoring Scoring, IReadOnlyList<ClassifyRule> Rules)
{
    public static RuleSet Empty { get; } = new(null, new RuleScoring(), Array.Empty<ClassifyRule>());

    /// <summary>规则权重基准（03 章 §5.3）：发件人精确=10，域名=8，主题正则=6，主题关键词=3。JSON 未显式给 weight 时采用。</summary>
    public static double DefaultWeight(RuleKind kind) => kind switch
    {
        RuleKind.SenderAddress => 10,
        RuleKind.SenderDomain => 8,
        RuleKind.SubjectRegex => 6,
        RuleKind.SubjectKeyword => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
