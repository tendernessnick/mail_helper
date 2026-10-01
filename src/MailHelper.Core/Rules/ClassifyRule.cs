using MailHelper.Core.Domain;

namespace MailHelper.Core.Rules;

/// <summary>分类规则（04 章 §2.3 签名；Category 为可空系 CHG-004 最小偏差：null = 仅重要度线索、不投类别票，对应 04 §7 示例 "category": null）。
/// Pattern 大小写不敏感；正则用 .NET 语法，匹配超时 100ms 保护（CLASS-001）。</summary>
public sealed record ClassifyRule(
    Guid Id,
    string Name,
    RuleKind Kind,
    string Pattern,
    string? Category,
    Importance? ImportanceHint,
    double Weight,
    int Priority,              // 小者先匹配；同优先级按 Weight 降序
    bool Enabled,
    RuleSource Source);
