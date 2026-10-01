namespace MailHelper.Core.Domain;

/// <summary>改判反馈记录（04 §3.2 CLASSIFICATION_FEEDBACK 实体；FR-11 纠正反馈闭环）。</summary>
public sealed record ClassificationFeedback(
    Guid Id,
    string MessageId,
    string OldCategory,
    string NewCategory,
    Importance OldImportance,
    Importance NewImportance,
    DateTime CreatedAtUtc);
