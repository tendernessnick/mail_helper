using MailHelper.Core;
using MailHelper.Core.Domain;

namespace MailHelper.Core.Services;

/// <summary>分类批次汇总（04 §2.2 引用未定义 → CHG-009 提案定义）。
/// 对应 04 §6 埋点 classify.completed：各类别分布 + 待确认数。</summary>
public sealed record ClassificationSummary(
    int Processed,
    IReadOnlyDictionary<MailCategory, int> CategoryCounts,
    int PendingReview,
    long ElapsedMs);
