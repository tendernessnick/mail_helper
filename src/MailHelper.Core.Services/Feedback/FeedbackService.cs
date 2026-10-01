using System.Security.Cryptography;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.Core.Services;

/// <summary>纠正反馈闭环（03 章 MOD-06 / 04 §2.2 签名）：改判该邮件立即生效（ClassifiedBy=user）→
/// 写 classification_feedback → 半自动提取发件人生成/覆盖规则（source=Feedback，×1.5 权重由引擎评分承担）→
/// 引擎内存态即时生效（Upsert）。规则名保留完整发件人（规则管理页可见，FR-11）；日志仅域名（04 §6 红线）。</summary>
public sealed class FeedbackService
{
    public const string ClassifiedByUser = "user";

    private readonly IMessageStore _messages;
    private readonly IRulesStore _rules;
    private readonly IFeedbackStore _feedback;
    private readonly RuleEngine _engine;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(
        IMessageStore messages,
        IRulesStore rules,
        IFeedbackStore feedback,
        RuleEngine engine,
        ILogger<FeedbackService>? logger = null)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? NullLogger<FeedbackService>.Instance;
    }

    /// <summary>应用改判（04 §2.2 签名）：邮件不存在返回 false；newImportance 省略时沿用原重要度。</summary>
    public async Task<bool> ApplyCorrectionAsync(
        string messageId, string newCategory, Importance? newImportance, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        var mail = await _messages.GetByIdAsync(messageId, ct);
        if (mail is null)
        {
            return false;
        }

        var importance = newImportance ?? mail.Importance;
        await _messages.ApplyClassificationRangeAsync(
        [
            new ClassificationWrite(messageId, newCategory, importance, Confidence: 1.0, ClassifiedByUser),
        ], ct); // FR-11：该邮件立即生效（EX-08：后续同步不覆盖人工分类）

        await _feedback.AddAsync(new ClassificationFeedback(
            Guid.NewGuid(), messageId, mail.Category, newCategory, mail.Importance, importance,
            DateTime.UtcNow), ct);

        if (mail.FromAddress is { Length: > 0 } fromAddress)
        {
            var rule = FeedbackRuleFor(fromAddress, newCategory, newImportance);
            await _rules.UpsertAsync(rule, ct);
            _engine.Upsert(rule); // 引擎即时生效：同发件人新邮件直接归新类别（TC-014）
            _logger.LogInformation(
                "feedback.rule_upserted source=Feedback domain={Domain} category={Category}",
                DomainOf(fromAddress), newCategory);
        }

        return true;
    }

    /// <summary>反馈规则：稳定 Id（同发件人重复改判覆盖而非堆积）、基准权重 10、Priority 0（先于内置）。</summary>
    internal static ClassifyRule FeedbackRuleFor(string fromAddress, string category, Importance? importanceHint)
    {
        return new ClassifyRule(
            Id: StableFeedbackRuleId(fromAddress),
            Name: $"feedback:{fromAddress}",
            Kind: RuleKind.SenderAddress,
            Pattern: fromAddress,
            Category: category,
            ImportanceHint: importanceHint,
            Weight: RuleSet.DefaultWeight(RuleKind.SenderAddress),
            Priority: 0,
            Enabled: true,
            Source: RuleSource.Feedback);
    }

    /// <summary>同发件人 → 同 Id（SHA256("feedback|"+小写地址) 前 16 字节，手法同 D-13）。</summary>
    internal static Guid StableFeedbackRuleId(string fromAddress)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("feedback|" + fromAddress.ToLowerInvariant()));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1 ? address[(at + 1)..] : "(invalid)";
    }
}
