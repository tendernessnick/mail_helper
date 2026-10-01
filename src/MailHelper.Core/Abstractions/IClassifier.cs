using MailHelper.Core.Domain;

namespace MailHelper.Core.Abstractions;

/// <summary>分类器扩展点（ADR-004：规则引擎为默认实现，LLM/ML 后插不改编译期依赖）。</summary>
public interface IClassifier
{
    /// <summary>"rule-engine" / "llm"（预留）。</summary>
    string Name { get; }

    Task<ClassificationResult> ClassifyAsync(ClassifiedInput input, CancellationToken ct);
}

/// <summary>分类结果（04 章 §2.1）。ClassifiedBy ∈ rule|user|llm（对应 DDL messages.classified_by）。</summary>
public sealed record ClassificationResult(
    string Category,
    Importance Importance,
    double Confidence,
    string ClassifiedBy);
