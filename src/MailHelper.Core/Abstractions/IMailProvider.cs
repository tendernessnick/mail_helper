using MailHelper.Core.Domain;

namespace MailHelper.Core.Abstractions;

/// <summary>邮件接入抽象（03 章 MOD-03：Graph/IMAP 双实现，统一输出 RemoteMessage 流与 deltaLink）。</summary>
public interface IMailProvider : IAsyncDisposable
{
    ChannelKind Kind { get; }

    IAsyncEnumerable<RemoteMessage> FetchDeltaAsync(string? deltaLink, int pageSize, CancellationToken ct);

    /// <summary>返回新 deltaLink（Graph）；IMAP 返回 UID 水位标记。</summary>
    Task<string?> CompleteAsync();

    /// <summary>授权健康检查。</summary>
    Task<ConnectionTestResult> TestAsync();
}
