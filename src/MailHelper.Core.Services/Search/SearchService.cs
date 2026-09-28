using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Core.Services;

/// <summary>搜索服务（03 章 MOD-09）：搜索语法解析 → FTS/过滤组合查询（FR-13；NFR-03 万级 P95&lt;500ms）。</summary>
public sealed class SearchService
{
    private readonly IMessageStore _store;
    private readonly string _accountId;

    public SearchService(IMessageStore store, string accountId)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
    }

    /// <summary>执行搜索；空查询返回空列表（UI 侧空态照常展示）。</summary>
    public Task<IReadOnlyList<MailMessage>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var parsed = SearchQueryParser.Parse(query);
        return parsed.IsEmpty
            ? Task.FromResult<IReadOnlyList<MailMessage>>(Array.Empty<MailMessage>())
            : _store.SearchAsync(_accountId, parsed, limit, ct);
    }
}
