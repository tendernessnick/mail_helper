namespace MailHelper.Core.Domain;

/// <summary>分类写回（messages 表 category/importance/confidence/classified_by/classified_at 五字段）。</summary>
public sealed record ClassificationWrite(
    string MessageId,
    string Category,
    Importance Importance,
    double Confidence,
    string ClassifiedBy);
