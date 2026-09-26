namespace MailHelper.Core.Domain;

/// <summary>
/// 分类器输入（04 章 §2.1 IClassifier.ClassifyAsync 引用但未定义 → CHG-003 最小偏差定义）。
/// BodyText 为 TextNormalizer 预处理后的纯文本（HTML 已转文本、引文与签名已剥离），可能为 null。
/// </summary>
public sealed record ClassifiedInput(
    string Subject,
    string FromName,
    string FromAddress,
    string? BodyText,
    DateTime? ReceivedAtUtc);
