using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.TextProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;

namespace MailHelper.Core.Services;

/// <summary>规则编辑失败（正则不可编译/模式为空/内置只读等）——UI 捕获后向用户展示 Message。</summary>
public sealed class RuleValidationException(string message) : Exception(message);

/// <summary>规则管理（FR-10 / 05 §3.3）：新建/编辑（Source=User）、删除（用户+反馈；内置只读）、
/// 启停（含内置——落 rules 表同 Id 行覆盖，重启后经启动合并持续生效）、试跑预览（最近 100 封）。</summary>
public sealed class RuleManagementService
{
    public const int PreviewLimit = 100;
    public const double MaxWeight = 15; // 03 §5.3 权重上限（D-09：基准 10 × Feedback 1.5）

    private readonly IRulesStore _rules;
    private readonly RuleEngine _engine;
    private readonly IMessageStore _messages;
    private readonly string _accountId;
    private readonly ILogger<RuleManagementService> _logger;

    public RuleManagementService(
        IRulesStore rules,
        RuleEngine engine,
        IMessageStore messages,
        string accountId,
        ILogger<RuleManagementService>? logger = null)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _logger = logger ?? NullLogger<RuleManagementService>.Instance;
    }

    /// <summary>新建或编辑自定义规则（编辑反馈规则时保持其 Source）；保存后引擎即时生效。</summary>
    public async Task<ClassifyRule> SaveAsync(ClassifyRule rule, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Validate(rule);
        await _rules.UpsertAsync(rule, ct);
        _engine.Upsert(rule);
        _logger.LogInformation("rule.saved id={RuleId} kind={Kind} category={Category}",
            rule.Id, rule.Kind, rule.Category?.ToString() ?? "-");
        return rule;
    }

    /// <summary>删除规则：仅用户/反馈规则（反馈删除=撤销学习，05 §3.3）；内置规则抛校验异常。</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var row = (await _rules.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
        if (row is null || row.Source == RuleSource.Builtin)
        {
            throw new RuleValidationException("内置规则不可删除（可禁用）");
        }

        await _rules.DeleteAsync(id, ct);
        _engine.Remove(id);
        _logger.LogInformation("rule.deleted id={RuleId} source={Source}", id, row.Source);
    }

    /// <summary>启停：内置规则首次禁用时按引擎内定义落表（同 Id 覆盖语义，重启合并后持续生效）。</summary>
    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var row = (await _rules.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id)
            ?? _engine.CurrentRules.FirstOrDefault(r => r.Id == id)
            ?? throw new RuleValidationException("规则不存在");
        if (row.Source == RuleSource.Builtin && !enabled)
        {
            _logger.LogInformation("rule.builtin_disabled id={RuleId}", id); // 09 §1.4：规则禁用可审计
        }

        var updated = row with { Enabled = enabled };
        await _rules.UpsertAsync(updated, ct);
        _engine.Upsert(updated);
    }

    /// <summary>全部活动规则（引擎当前快照：内置 ∪ 用户 ∪ 反馈）——规则管理页列表数据源。</summary>
    public IReadOnlyList<ClassifyRule> ListAll() => _engine.CurrentRules;

    /// <summary>试跑预览（FR-10）：草稿规则对最近 100 封邮件的命中结果（不影响已存规则）。</summary>
    public async Task<IReadOnlyList<MailMessage>> PreviewAsync(ClassifyRule draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Validate(draft);
        var mails = await _messages.GetInboxAsync(_accountId, new InboxQuery(Limit: PreviewLimit), ct);
        var hits = new List<MailMessage>();
        foreach (var mail in mails)
        {
            var input = new ClassifiedInput(
                mail.Subject ?? string.Empty,
                mail.FromName ?? string.Empty,
                mail.FromAddress ?? string.Empty,
                TextNormalizer.Normalize(mail.BodyPreview),
                mail.ReceivedAtUtc);
            if (_engine.Matches(input, draft))
            {
                hits.Add(mail);
            }
        }

        return hits;
    }

    private static void Validate(ClassifyRule rule)
    {
        if (rule.Source == RuleSource.Builtin)
        {
            throw new RuleValidationException("内置规则只读（可禁用，不可新建/编辑）");
        }

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            throw new RuleValidationException("匹配模式不能为空");
        }

        if (rule.Weight is < 0 or > MaxWeight)
        {
            throw new RuleValidationException($"权重需在 0–{MaxWeight} 之间");
        }

        if (rule.Kind == RuleKind.SubjectRegex)
        {
            try
            {
                _ = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    RuleEngine.RegexMatchTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new RuleValidationException($"正则表达式无效：{ex.Message}");
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuleValidationException("正则表达式编译超时，请简化模式（防 ReDoS 保护）");
            }
        }
    }
}
