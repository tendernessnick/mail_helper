using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Storage;
using MailHelper.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace MailHelper.Integration.Tests;

/// <summary>Outlook 桌面通道（FR-02 落地路径调整 CHG-011）：复用经典 Outlook 本机登录态（COM），
/// 绕开被租户禁止的 OAuth 用户同意；断点按收件时间水位（回退 24h 晚到窗口，upsert 幂等防重）。</summary>
public class OutlookDesktopProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-outlook-" + Guid.NewGuid().ToString("N"));
    private readonly FakeOutlookSource _source = new();
    private readonly BodyCacheStore _bodyCache;

    public OutlookDesktopProviderTests()
    {
        Directory.CreateDirectory(_dir);
        _bodyCache = new BodyCacheStore(Path.Combine(_dir, "bodies"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private OutlookDesktopMailProvider NewProvider(IBodyCache? bodyCache = null) =>
        new(_source, bodyCache, cacheAccountKey: "acc-1");

    private static DateTimeOffset At(int month, int day, int hour = 8) =>
        new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

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

    [Fact]
    public async Task FirstSync_ReturnsAllAsAdded_WithEntryIdKey()
    {
        _source.Add("eid-1", subject: "Tuition", from: "bursary@hku.hk", received: At(9, 1), unread: true, attachments: 1);
        _source.Add("eid-2", subject: "选课通知", from: "moodle.hku.hk", received: At(9, 2));
        var provider = NewProvider();

        var mails = await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));

        mails.Should().HaveCount(2);
        mails[0].ProviderMessageId.Should().Be("outlook:eid-1"); // EntryID 全局唯一键
        mails[0].Kind.Should().Be(ChangeKind.Added);
        mails[0].HasAttachments.Should().BeTrue();
        mails[1].IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task Incremental_FetchesOnlyAfterWatermark()
    {
        _source.Add("a", received: At(9, 1));
        _source.Add("b", received: At(9, 2));
        var provider = NewProvider();
        await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));
        _source.Add("c", received: At(9, 3));

        var mails = await CollectAsync(provider.FetchDeltaAsync(
            OutlookDesktopMailProvider.EncodeBreakpoint(At(9, 2)), 10, CancellationToken.None));

        mails.Select(m => m.ProviderMessageId).Should().BeEquivalentTo(["outlook:b", "outlook:c"]); // b 在 24h 晚到窗口内被重拉（upsert 幂等吸收）
        var expectedWindow = At(9, 2).Subtract(TimeSpan.FromHours(24)).UtcDateTime;
        _source.LastSince.Should().Be(expectedWindow);
    }

    [Fact]
    public async Task Preview_TruncatedTo500_HtmlCached()
    {
        _source.Add("h", htmlBody: "<html><body><p>hello</p></body></html>",
            textBody: new string('长', 600));
        var provider = NewProvider(_bodyCache);

        var mails = await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));

        mails[0].BodyPreview.Should().HaveLength(500); // 02 §9 预览截断
        mails[0].HtmlPath.Should().NotBeNull(); // HTML 正文入磁盘缓存 → 阅读窗格完整渲染
        (await _bodyCache.ReadAsync(mails[0].HtmlPath!, CancellationToken.None)).Should().Contain("hello");
    }

    [Fact]
    public async Task Complete_ReturnsWatermarkBreakpoint()
    {
        _source.Add("a", received: At(9, 1, 10));
        _source.Add("b", received: At(9, 1, 12));
        var provider = NewProvider();
        await CollectAsync(provider.FetchDeltaAsync(null, 10, CancellationToken.None));

        var link = await provider.CompleteAsync();

        link.Should().Be(OutlookDesktopMailProvider.EncodeBreakpoint(At(9, 1, 12)));
    }

    [Fact]
    public async Task InvalidBreakpoint_ResetsToFullSync()
    {
        _source.Add("a", received: At(9, 1));
        var provider = NewProvider();

        var mails = await CollectAsync(provider.FetchDeltaAsync("garbage-link", 10, CancellationToken.None));

        mails.Should().HaveCount(1); // 非法断点安全侧全量（D-59 同语义）
    }

    [Fact]
    public async Task Paging_YieldsInBatches()
    {
        for (var i = 0; i < 5; i++)
        {
            _source.Add($"e{i}", received: At(9, 1).AddMinutes(i));
        }

        var provider = NewProvider();
        var mails = await CollectAsync(provider.FetchDeltaAsync(null, 2, CancellationToken.None));

        mails.Should().HaveCount(5);
        _source.BatchSizes.Should().OnlyContain(n => n == 2);
    }

    [Fact]
    public async Task TestAsync_SourceReachable_ReturnsSuccess()
    {
        var result = await NewProvider().TestAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TestAsync_OutlookUnavailable_ReportsError()
    {
        _source.ConnectException = new InvalidOperationException("Outlook 未运行或未登录");
        var result = await NewProvider().TestAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("SYNC-001");
    }

    [Fact]
    public async Task GetAccountAddressAsync_ReturnsLoginAddress_ForConnectFlow()
    {
        // CHG-011 连接流程：跳过 OAuth，直接读 Outlook 登录态地址落库
        var address = await NewProvider().GetAccountAddressAsync(CancellationToken.None);

        address.Should().Be("ruijiehu7-c@my.cityu.edu.hk");
    }

    [Fact]
    public async Task GetAccountAddressAsync_OutlookUnavailable_PropagatesFailure()
    {
        _source.ConnectException = new InvalidOperationException("Outlook 未运行或未登录");

        var act = () => NewProvider().GetAccountAddressAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>内存 Outlook 数据源假件（真实实现=OutlookComMailSource，COM 路径本机验证）。</summary>
    private sealed class FakeOutlookSource : IOutlookMailSource
    {
        private readonly List<OutlookMessageSummary> _messages = [];

        public List<int> BatchSizes { get; } = [];
        public DateTime? LastSince { get; private set; }
        public Exception? ConnectException { get; set; }

        public void Add(string entryId, string subject = "S", string from = "f@hku.hk",
            DateTimeOffset received = default, bool unread = true, int attachments = 0,
            string? internetMessageId = null, string textBody = "body", string htmlBody = "")
        {
            _messages.Add(new OutlookMessageSummary(
                entryId, internetMessageId ?? $"<{entryId}@hku.hk>", subject, "Sender", from,
                received == default ? DateTimeOffset.UtcNow : received,
                IsRead: !unread, // record 位置参数对位：IsRead
                AttachmentCount: attachments, TextBody: textBody, HtmlBody: htmlBody));
        }

        public string GetAccountAddress()
        {
            if (ConnectException is not null)
            {
                throw ConnectException;
            }

            return "ruijiehu7-c@my.cityu.edu.hk";
        }

        public IReadOnlyList<OutlookMessageSummary> FetchInboxSince(DateTime? sinceUtc, int take)
        {
            if (ConnectException is not null)
            {
                throw ConnectException;
            }

            BatchSizes.Add(take);
            LastSince = sinceUtc;
            return _messages
                .Where(m => sinceUtc is null || m.ReceivedAtUtc > sinceUtc)
                .OrderBy(m => m.ReceivedAtUtc)
                .Take(take)
                .ToList();
        }

        public void Dispose() { }
    }
}
