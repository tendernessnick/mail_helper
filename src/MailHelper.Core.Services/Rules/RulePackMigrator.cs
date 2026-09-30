using MailHelper.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.Core.Services.Rules;

/// <summary>S13-B 内置规则包版本迁移：内置包版本与已应用版本不一致时——
/// ① 把 rule 来源的已分类邮件置回待处理（分类管线用新规则重跑）；
/// ② 清空同步断点（下一轮全量重拉，顺带回填历史缺失的发件地址等同步字段）；
/// ③ 记录新版本。用户改判（classified_by='user'）与反馈规则永不触碰（EX-08 精神）。</summary>
public sealed class RulePackMigrator(
    IMessageStore store,
    IAccountStore accounts,
    ISettingsStore settings,
    ILogger<RulePackMigrator>? logger = null)
{
    /// <summary>Settings 键：已应用的内置规则包版本。</summary>
    public const string AppliedVersionKey = "rules.builtin.applied_version";

    private readonly IMessageStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IAccountStore _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ISettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<RulePackMigrator> _logger = logger ?? NullLogger<RulePackMigrator>.Instance;

    /// <summary>按需迁移。版本一致时不做任何事，返回 0；返回值为重置回炉的邮件数。</summary>
    public async Task<int> MigrateIfNeededAsync(string builtinVersion, string accountId, CancellationToken ct)
    {
        var applied = await _settings.GetAsync(AppliedVersionKey, ct);
        if (applied == builtinVersion)
        {
            return 0;
        }

        var reset = await _store.ResetRuleClassificationAsync(accountId, ct);
        var checkpoint = await _accounts.GetCheckpointAsync(accountId, ct);
        if (checkpoint is not null && (checkpoint.DeltaLink is not null || checkpoint.ImapUidWatermark is not null))
        {
            await _accounts.SaveCheckpointAsync(checkpoint with { DeltaLink = null, ImapUidWatermark = null }, ct);
        }

        await _settings.SetAsync(AppliedVersionKey, builtinVersion, ct);
        _logger.LogInformation(
            "rules.pack_migrated version={Version} reset_count={ResetCount}", builtinVersion, reset);
        return reset;
    }
}
