using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using Microsoft.EntityFrameworkCore;

namespace MailHelper.Infrastructure.Storage;

/// <summary>rules 表仓储（IRulesStore 实现；04 §3.2 DDL。内置规则包不走本表——随包 JSON 只读层，D-40）。</summary>
public sealed class RuleRepository : IRulesStore
{
    private readonly string _dbPath;

    public RuleRepository(string dbPath) => _dbPath = dbPath;

    public async Task<IReadOnlyList<ClassifyRule>> GetAllAsync(CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var rows = await db.Rules.AsNoTracking().ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var key = id.ToString();
            await db.Rules.Where(r => r.Id == key).ExecuteDeleteAsync(ct);
        }, ct);

    public async Task UpsertAsync(ClassifyRule rule, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var existing = await db.Rules.FirstOrDefaultAsync(r => r.Id == rule.Id.ToString(), ct);
            if (existing is null)
            {
                db.Rules.Add(ToEntity(rule));
            }
            else
            {
                CopySyncFields(existing, rule);
            }

            await db.SaveChangesAsync(ct);
        }, ct);

    private static ClassifyRule ToDomain(RuleEntity r) => new(
        Id: Guid.Parse(r.Id),
        Name: r.Name,
        Kind: Enum.TryParse<RuleKind>(r.Kind, ignoreCase: true, out var kind) ? kind : RuleKind.SubjectKeyword,
        Pattern: r.Pattern,
        Category: Enum.TryParse<MailCategory>(r.Category, ignoreCase: true, out var category) ? category : null,
        ImportanceHint: r.ImportanceHint is { } hint ? (Importance)hint : null,
        Weight: r.Weight,
        Priority: r.Priority,
        Enabled: r.Enabled,
        Source: Enum.TryParse<RuleSource>(r.Source, ignoreCase: true, out var source) ? source : RuleSource.Builtin);

    private static RuleEntity ToEntity(ClassifyRule r) => new()
    {
        Id = r.Id.ToString(),
        Name = r.Name,
        Kind = r.Kind.ToString(),
        Pattern = r.Pattern,
        Category = r.Category?.ToString(),
        ImportanceHint = r.ImportanceHint is { } hint ? (int)hint : null,
        Weight = r.Weight,
        Priority = r.Priority,
        Enabled = r.Enabled,
        Source = r.Source.ToString(),
        UpdatedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    };

    private static void CopySyncFields(RuleEntity row, ClassifyRule r)
    {
        row.Name = r.Name;
        row.Kind = r.Kind.ToString();
        row.Pattern = r.Pattern;
        row.Category = r.Category?.ToString();
        row.ImportanceHint = r.ImportanceHint is { } hint ? (int)hint : null;
        row.Weight = r.Weight;
        row.Priority = r.Priority;
        row.Enabled = r.Enabled;
        row.Source = r.Source.ToString();
        row.UpdatedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}

/// <summary>classification_feedback 表仓储（IFeedbackStore 实现；同 id 幂等，防重复提交）。</summary>
public sealed class FeedbackRepository : IFeedbackStore
{
    private readonly string _dbPath;

    public FeedbackRepository(string dbPath) => _dbPath = dbPath;

    public async Task AddAsync(ClassificationFeedback feedback, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var id = feedback.Id.ToString();
            if (await db.ClassificationFeedback.AnyAsync(f => f.Id == id, ct))
            {
                return;
            }

            db.ClassificationFeedback.Add(new ClassificationFeedbackEntity
            {
                Id = id,
                MessageId = feedback.MessageId,
                OldCategory = feedback.OldCategory.ToString(),
                NewCategory = feedback.NewCategory.ToString(),
                OldImportance = (int)feedback.OldImportance,
                NewImportance = (int)feedback.NewImportance,
                CreatedAtUtc = new DateTimeOffset(feedback.CreatedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
            });
            await db.SaveChangesAsync(ct);
        }, ct);

    public Task<int> CountAsync(CancellationToken ct) =>
        MailDatabase.WithDbAsync(_dbPath, async db => await db.ClassificationFeedback.CountAsync(ct), ct);
}
