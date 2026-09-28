using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Auth;
using MailHelper.Infrastructure.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace MailHelper.Integration.Tests;

/// <summary>IMAP 兜底通道（FR-02 / 04 §4.3 / 03 §5.4）：XOAUTH2 令牌 + UIDVALIDITY/UID 水位增量 +
/// 统一 RemoteMessage 输出。FakeImapAdapter 驱动不出网；真实连通=检查点②。</summary>
public class ImapMailProviderTests
{
    private const string Email = "s@connect.hku.hk";

    private static AuthResult OkToken() => AuthResult.Ok(new AuthToken(
        "imap-access-token", DateTimeOffset.UtcNow.AddHours(1), Email, Email, "t1", ["IMAP.AccessAsUser.All"]));

    private static FakeTokenProvider NewTokens() => new() { SilentResult = OkToken() };

    [Fact]
    public async Task FirstSync_NoCheckpoint_ReturnsAllAsAdded_WithImapIdFormat()
    {
        var adapter = new FakeImapAdapter(uidValidity: 7);
        adapter.Add(1, subject: "Tuition", from: "bursary@hku.hk", seen: false, attachments: true);
        adapter.Add(2, subject: "选课通知", from: "moodle.hku.hk", seen: true);
        var provider = new ImapMailProvider(NewTokens(), () => adapter, Email);

        var mails = await CollectAsync(provider.FetchDeltaAsync(deltaLink: null, pageSize: 10, CancellationToken.None));

        mails.Should().HaveCount(2);
        mails[0].ProviderMessageId.Should().Be($"imap:{Email}:INBOX:1"); // 04 §3.1 复合键
        mails[0].Kind.Should().Be(ChangeKind.Added); // IMAP 无远端删除感知（03 §5.4）
        mails[0].Subject.Should().Be("Tuition");
        mails[0].HasAttachments.Should().BeTrue();
        mails[1].IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task Incremental_FetchesOnlyUidsAboveWatermark()
    {
        var adapter = new FakeImapAdapter(uidValidity: 7);
        adapter.Add(1); adapter.Add(2); adapter.Add(3);
        var provider = new ImapMailProvider(NewTokens(), () => adapter, Email);
        await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));
        adapter.Add(4); adapter.Add(5);

        await CollectAsync(provider.FetchDeltaAsync(
            ImapMailProvider.EncodeBreakpoint(uidValidity: 7, lastUid: 3), 10, CancellationToken.None));

        adapter.FetchMinUids.Should().Equal([0u, 3u]); // 首轮全量 minUid=0，增量轮从水位 3 起（04 §4.3）
    }

    [Fact]
    public async Task UidValidityChanged_ResetsToFullSync()
    {
        var adapter = new FakeImapAdapter(uidValidity: 9);
        adapter.Add(10); adapter.Add(11);
        var provider = new ImapMailProvider(NewTokens(), () => adapter, Email);

        var mails = await CollectAsync(provider.FetchDeltaAsync(
            ImapMailProvider.EncodeBreakpoint(uidValidity: 7, lastUid: 3), 10, CancellationToken.None));

        mails.Should().HaveCount(2); // 旧断点作废 → 全量（SYNC-003 等价语义）
        adapter.FetchMinUids[0].Should().Be(0);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsMaxUidBreakpoint()
    {
        var adapter = new FakeImapAdapter(uidValidity: 7);
        adapter.Add(1); adapter.Add(5);
        var provider = new ImapMailProvider(NewTokens(), () => adapter, Email);
        await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));

        var link = await provider.CompleteAsync();

        link.Should().Be(ImapMailProvider.EncodeBreakpoint(uidValidity: 7, lastUid: 5));
    }

    [Fact]
    public async Task Mapping_InternetMessageId_StripsAngleBrackets()
    {
        var adapter = new FakeImapAdapter(uidValidity: 7);
        adapter.Add(1, internetMessageId: "<abc@hku.hk>");
        var provider = new ImapMailProvider(NewTokens(), () => adapter, Email);

        var mails = await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));

        mails[0].InternetMessageId.Should().Be("abc@hku.hk"); // 与 Graph 通道口径一致（无尖括号）
    }

    [Fact]
    public async Task Preview_TruncatedTo500Chars()
    {
        var adapter = new FakeImapAdapter(uidValidity: 7);
        adapter.Add(1, textBody: new string('预', 600));
        var provider = new ImapMailProvider(NewTokens(), () => adapter, Email);

        var mails = await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));

        mails[0].BodyPreview.Should().HaveLength(500); // 02 §9：预览仅存前 500 字符
    }

    [Fact]
    public async Task Paging_YieldsInBatchSizeChunks()
    {
        var adapter = new FakeImapAdapter(uidValidity: 7);
        for (uint uid = 1; uid <= 5; uid++)
        {
            adapter.Add(uid);
        }

        var provider = new ImapMailProvider(NewTokens(), () => adapter, Email);
        var mails = await CollectAsync(provider.FetchDeltaAsync(null, pageSize: 2, CancellationToken.None));

        mails.Should().HaveCount(5);
        adapter.LastFetchBatchSize.Should().Be(2);
    }

    [Fact]
    public async Task AuthFailureAfterRefreshRetry_ThrowsSync004()
    {
        var tokens = new FakeTokenProvider
        {
            SilentResult = OkToken(),
            RefreshedSilentResult = OkToken(),
        };
        var adapter = new FakeImapAdapter(uidValidity: 7)
        {
            ConnectException = new MailKit.Security.AuthenticationException("XOAUTH2 failed"),
        };
        var provider = new ImapMailProvider(tokens, () => adapter, Email);

        var act = async () => await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));

        var ex = await act.Should().ThrowAsync<MailProviderException>();
        ex.Which.ErrorCode.Should().Be("SYNC-004"); // 04 §5：IMAP 认证失败
        tokens.SilentCalls.Should().BeGreaterThanOrEqualTo(2); // 401 后强刷重试一次（D-31 语义）
    }

    [Fact]
    public async Task NetworkFailure_MapsToSync001()
    {
        var adapter = new FakeImapAdapter(uidValidity: 7)
        {
            ConnectException = new System.Net.Sockets.SocketException(),
        };
        var provider = new ImapMailProvider(NewTokens(), () => adapter, Email);

        var act = async () => await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));

        var ex = await act.Should().ThrowAsync<MailProviderException>();
        ex.Which.ErrorCode.Should().Be("SYNC-001"); // 网络不可达 → 离线态
    }

    [Fact]
    public async Task SilentRefreshFails_ThrowsAuth003()
    {
        var tokens = new FakeTokenProvider(); // 无 SilentResult → AUTH-003
        var provider = new ImapMailProvider(tokens, () => new FakeImapAdapter(7), Email);

        var act = async () => await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));

        (await act.Should().ThrowAsync<MailProviderException>()).Which.ErrorCode.Should().Be("AUTH-003");
    }

    [Fact]
    public async Task TestAsync_ConnectSucceeds_ReturnsSuccess()
    {
        var provider = new ImapMailProvider(NewTokens(), () => new FakeImapAdapter(7), Email);

        var result = await provider.TestAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TestAsync_AuthFailure_ReportsSync004()
    {
        var adapter = new FakeImapAdapter(uidValidity: 7)
        {
            ConnectException = new MailKit.Security.AuthenticationException("denied"),
        };
        var provider = new ImapMailProvider(NewTokens(), () => adapter, Email);

        var result = await provider.TestAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("SYNC-004");
    }

    private static async Task<List<RemoteMessage>> CollectAsync(
        IAsyncEnumerable<RemoteMessage> source, CancellationToken ct = default)
    {
        var list = new List<RemoteMessage>();
        await foreach (var mail in source.WithCancellation(ct))
        {
            list.Add(mail);
        }

        return list;
    }

    /// <summary>内存 IMAP 适配器假件：预置消息与可变 UIDVALIDITY；记录 Fetch 调用以断言增量语义。</summary>
    private sealed class FakeImapAdapter(uint uidValidity) : IImapClientAdapter
    {
        private readonly Dictionary<uint, (ImapMessageSummary Summary, string Text)> _messages = [];

        public List<uint> FetchMinUids { get; } = [];
        public int LastFetchBatchSize { get; private set; }
        public Exception? ConnectException { get; init; }

        public void Add(uint uid, string subject = "S", string from = "f@hku.hk", bool seen = false,
            bool attachments = false, string? internetMessageId = null, string textBody = "body")
        {
            _messages[uid] = (
                new ImapMessageSummary(uid, internetMessageId, subject, "Sender", from,
                    new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), seen, attachments),
                textBody);
        }

        public Task ConnectAsync(string user, string accessToken, CancellationToken ct)
        {
            if (ConnectException is not null)
            {
                throw ConnectException;
            }

            return Task.CompletedTask;
        }

        public Task<uint> GetUidValidityAsync(CancellationToken ct) => Task.FromResult(uidValidity);

        public Task<IReadOnlyList<ImapMessageSummary>> FetchSummariesAsync(uint minUid, int batchSize, CancellationToken ct)
        {
            FetchMinUids.Add(minUid);
            LastFetchBatchSize = batchSize;
            var summaries = _messages
                .Where(kv => kv.Key > minUid)
                .OrderBy(kv => kv.Key)
                .Take(batchSize)
                .Select(kv => kv.Value.Summary)
                .ToList();
            return Task.FromResult<IReadOnlyList<ImapMessageSummary>>(summaries);
        }

        public Task<string> FetchTextPreviewAsync(uint uid, int maxChars, CancellationToken ct) =>
            Task.FromResult(_messages[uid].Text);

        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
