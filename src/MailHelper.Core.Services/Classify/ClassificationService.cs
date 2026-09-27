using System.Diagnostics;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.TextProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.Core.Services;

/// <summary>分类管线（04 章 §2.2）：预处理 → IClassifier（rule-engine 为主）→ 待确认判定 → 落库。
/// 置信度低于 ReviewThreshold（默认 0.55，D-10）计入待确认队列（FR-09；查询视图随 S6 落地）。</summary>
public sealed class ClassificationService
{
    public const double DefaultReviewThreshold = 0.55;

    private readonly IClassifier _classifier;
    private readonly IMessageStore _store;
    private readonly string _accountId;
    private readonly ILogger<ClassificationService> _logger;
    private readonly double _reviewThreshold;

    public ClassificationService(
        IClassifier classifier,
        IMessageStore store,
        string accountId,
        ILogger<ClassificationService>? logger = null,
        double? reviewThreshold = null)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _logger = logger ?? NullLogger<ClassificationService>.Instance;
        _reviewThreshold = reviewThreshold ?? DefaultReviewThreshold;
    }

    public async Task<ClassificationSummary> ClassifyPendingAsync(int batchSize, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var pending = await _store.GetPendingClassificationAsync(_accountId, batchSize, ct);
        var writes = new List<ClassificationWrite>(pending.Count);

        foreach (var mail in pending)
        {
            var input = new ClassifiedInput(
                mail.Subject ?? string.Empty,
                mail.FromName ?? string.Empty,
                mail.FromAddress ?? string.Empty,
                TextNormalizer.Normalize(mail.BodyPreview), // 管线预处理：剥引文/签名（03 §5.3 B 节点）
                mail.ReceivedAtUtc);
            var result = await _classifier.ClassifyAsync(input, ct);
            writes.Add(new ClassificationWrite(mail.Id, result.Category, result.Importance, result.Confidence, result.ClassifiedBy));
        }

        if (writes.Count > 0)
        {
            await _store.ApplyClassificationRangeAsync(writes, ct);
        }

        var pendingReview = writes.Count(w => w.Confidence < _reviewThreshold);
        var counts = Enum.GetValues<MailCategory>()
            .Where(c => writes.Any(w => w.Category == c))
            .ToDictionary(c => c, c => writes.Count(w => w.Category == c));

        _logger.LogInformation(
            "classify.completed processed={Processed} pending_review={PendingReview} categories={Categories}",
            writes.Count, pendingReview, string.Join(",", counts.Select(kv => $"{kv.Key}={kv.Value}")));

        return new ClassificationSummary(writes.Count, counts, pendingReview, sw.ElapsedMilliseconds);
    }
}
