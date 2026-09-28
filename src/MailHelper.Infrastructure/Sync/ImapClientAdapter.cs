using System.Net.Sockets;
using MailHelper.Core.Abstractions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MailSearchQuery = MailKit.Search.SearchQuery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace MailHelper.Infrastructure.Sync;

/// <summary>IMAP 会话抽象（测试假件替换点；真实实现 MailKit，网络路径=检查点②验证，先例同 D-27）。</summary>
public interface IImapClientAdapter : IAsyncDisposable
{
    Task ConnectAsync(string user, string accessToken, CancellationToken ct);

    Task<uint> GetUidValidityAsync(CancellationToken ct);

    /// <summary>取 UID &gt; minUid 的消息摘要（升序、至多 batchSize 条），等价 UID SEARCH UID {minUid+1}:*。</summary>
    Task<IReadOnlyList<ImapMessageSummary>> FetchSummariesAsync(uint minUid, int batchSize, CancellationToken ct);

    Task<string> FetchTextPreviewAsync(uint uid, int maxChars, CancellationToken ct);

    Task DisconnectAsync(CancellationToken ct);
}

/// <summary>IMAP 消息摘要（04 §4.3 FETCH (ENVELOPE|FLAGS|BODYSTRUCTURE) 组装来源）。</summary>
public sealed record ImapMessageSummary(
    uint Uid,
    string? InternetMessageId,
    string? Subject,
    string? FromName,
    string? FromAddress,
    DateTime? ReceivedAtUtc,
    bool IsRead,
    bool HasAttachments);

/// <summary>MailKit IMAP 适配器（04 §4.3：outlook.office365.com:993 SSL，SASL XOAUTH2）。</summary>
public sealed class ImapKitClientAdapter(ILogger<ImapKitClientAdapter>? logger = null) : IImapClientAdapter
{
    public const string Host = ImapMailProvider.ImapHost;
    public const int Port = ImapMailProvider.ImapPort;

    private readonly ImapClient _client = new();
    private readonly ILogger<ImapKitClientAdapter> _logger = logger ?? NullLogger<ImapKitClientAdapter>.Instance;
    private IMailFolder? _inbox;

    public async Task ConnectAsync(string user, string accessToken, CancellationToken ct)
    {
        await _client.ConnectAsync(Host, Port, MailKit.Security.SecureSocketOptions.SslOnConnect, ct);
        await _client.AuthenticateAsync(new SaslMechanismOAuth2(user, accessToken), ct);
        _inbox = _client.Inbox ?? throw new InvalidOperationException("IMAP 服务器未提供 INBOX 文件夹");
        await _inbox.OpenAsync(FolderAccess.ReadOnly, ct); // v1 固定 INBOX（D-62）
    }

    public Task<uint> GetUidValidityAsync(CancellationToken ct)
    {
        EnsureInbox();
        return Task.FromResult((uint)_inbox!.UidValidity);
    }

    public async Task<IReadOnlyList<ImapMessageSummary>> FetchSummariesAsync(uint minUid, int batchSize, CancellationToken ct)
    {
        EnsureInbox();
        var allUids = await _inbox!.SearchAsync(MailSearchQuery.All, ct);
        var page = allUids
            .Where(u => u.Id > minUid) // UID SEARCH {minUid+1}:* 等价语义（04 §4.3）
            .OrderBy(u => u.Id)
            .Take(batchSize)
            .ToList();
        if (page.Count == 0)
        {
            return [];
        }

        var fetched = await _inbox.FetchAsync(page,
            MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.BodyStructure, ct);
        var summaries = new List<ImapMessageSummary>(fetched.Count);
        foreach (var item in fetched)
        {
            var envelope = item.Envelope ?? throw new InvalidOperationException("Fetch(ENVELOPE) 未返回信封");
            summaries.Add(new ImapMessageSummary(
                Uid: item.UniqueId.Id,
                InternetMessageId: envelope.MessageId,
                Subject: envelope.Subject,
                FromName: envelope.From.Count > 0 && envelope.From[0] is MailboxAddress from ? from.Name : null,
                FromAddress: envelope.From.Count > 0 && envelope.From[0] is MailboxAddress addr ? addr.Address : null,
                ReceivedAtUtc: envelope.Date.HasValue
                    ? envelope.Date.Value.UtcDateTime
                    : item.Date.UtcDateTime,
                IsRead: item.Flags.HasValue && item.Flags.Value.HasFlag(MessageFlags.Seen),
                HasAttachments: item.Attachments.Any()));
        }

        return summaries;
    }

    public async Task<string> FetchTextPreviewAsync(uint uid, int maxChars, CancellationToken ct)
    {
        EnsureInbox();
        var message = await _inbox!.GetMessageAsync(new UniqueId(uid), ct);
        var text = message.TextBody ?? message.HtmlBody ?? string.Empty;
        return text.Length > maxChars * 4 ? text[..(maxChars * 4)] : text; // HTML→文本前的粗截，防超大正文（EX-06 意识）
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync(true, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None);
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private void EnsureInbox() =>
        _ = _inbox ?? throw new InvalidOperationException("IMAP 会话尚未打开（先 ConnectAsync）");
}
