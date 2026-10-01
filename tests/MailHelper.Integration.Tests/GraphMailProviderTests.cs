using System.Text;
using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Auth;
using MailHelper.Infrastructure.Storage;
using MailHelper.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>Graph REST v1.0 契约测试（04 章 §4.1/§4.2；WireMock 不出网）。
/// 覆盖 06 章 EX-TC-01（429）、EX-TC-02（401）、EX-TC-03（deltaLink 410）、EX-TC-04（断网）等异常流。</summary>
public class GraphMailProviderTests : IDisposable
{
    private const string InitialPath = "/v1.0/me/mailFolders/inbox/messages/delta";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-graph-" + Guid.NewGuid().ToString("N"));
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly FakeTokenProvider _tokens = new();

    public GraphMailProviderTests()
    {
        Directory.CreateDirectory(_dir);
        _tokens.SilentResult = AuthResult.Ok(Token("tok-1"));
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string Base => _server.Url!;

    private static AuthToken Token(string accessToken) => new(
        accessToken, DateTimeOffset.UtcNow.AddHours(1), "acct", "s@connect.hku.hk", "tenant", new[] { "Mail.Read" });

    private GraphMailProvider NewProvider(IBodyCache? cache = null, Func<RemoteMessage, bool>? detector = null, Uri? baseAddress = null) =>
        new(_tokens,
            baseAddress ?? new Uri(Base),
            new GraphHttpOptions
            {
                BackoffDelays = new[]
                {
                    TimeSpan.FromMilliseconds(5),
                    TimeSpan.FromMilliseconds(5),
                    TimeSpan.FromMilliseconds(5),
                },
            },
            cache,
            detector);

    private void Stub(string path, string body) =>
        _server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private void StubStatus(string path, int status, string? retryAfter = null) =>
        _server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(BuildStatusResponse(status, retryAfter));

    private static IResponseBuilder BuildStatusResponse(int status, string? retryAfter)
    {
        var response = Response.Create().WithStatusCode(status);
        return retryAfter is null ? response : response.WithHeader("Retry-After", retryAfter);
    }

    private int CountRequests(string path) =>
        _server.LogEntries.Count(le => le.RequestMessage.Path == path);

    private static string MsgJson(string id, string subject = "Hello", string preview = "preview",
        string address = "someone@hku.hk", bool hasAttachments = false) =>
        "{\"id\":\"" + id + "\",\"internetMessageId\":\"<" + id + "@im>\",\"subject\":\"" + subject + "\"," +
        "\"from\":{\"emailAddress\":{\"name\":\"Sender\",\"address\":\"" + address + "\"}}," +
        "\"receivedDateTime\":\"2026-09-26T08:00:00Z\",\"isRead\":false,\"hasAttachments\":" + (hasAttachments ? "true" : "false") + "," +
        "\"bodyPreview\":\"" + preview + "\",\"changeKey\":\"ck-" + id + "\"}";

    private static string PageJson(IReadOnlyList<string> items, string? nextLink = null, string? deltaLink = null)
    {
        var sb = new StringBuilder("{\"value\":[");
        sb.Append(string.Join(",", items));
        sb.Append(']');
        if (nextLink is not null)
        {
            sb.Append(",\"@odata.nextLink\":\"").Append(nextLink).Append('"');
        }

        if (deltaLink is not null)
        {
            sb.Append(",\"@odata.deltaLink\":\"").Append(deltaLink).Append('"');
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static async Task<List<RemoteMessage>> CollectAsync(GraphMailProvider provider, string? deltaLink, int pageSize = 100)
    {
        var messages = new List<RemoteMessage>();
        await foreach (var message in provider.FetchDeltaAsync(deltaLink, pageSize, CancellationToken.None))
        {
            messages.Add(message);
        }

        return messages;
    }

    [Fact]
    public async Task FullSync_FollowsNextLinks_ToDeltaLink()
    {
        var nextLink = Base + "/v1.0/deltaPage2";
        var deltaLink = Base + "/v1.0/deltaDone?token=d1";
        Stub(InitialPath, PageJson(new[] { MsgJson("a1"), MsgJson("a2") }, nextLink: nextLink));
        Stub("/v1.0/deltaPage2", PageJson(new[] { MsgJson("a3") }, deltaLink: deltaLink));

        await using var provider = NewProvider();
        var messages = await CollectAsync(provider, null, pageSize: 100);

        messages.Should().HaveCount(3);
        (await provider.CompleteAsync()).Should().Be(deltaLink);
        CountRequests("/v1.0/deltaPage2").Should().Be(1); // nextLink 被跟随
    }

    [Fact]
    public async Task DeltaSync_RequestsProvidedDeltaLink()
    {
        var oldLink = Base + "/v1.0/deltaOld?token=old";
        var newLink = Base + "/v1.0/deltaNew?token=new";
        Stub("/v1.0/deltaOld", PageJson(new[] { MsgJson("d1") }, deltaLink: newLink));

        await using var provider = NewProvider();
        var messages = await CollectAsync(provider, oldLink);

        messages.Should().ContainSingle(m => m.ProviderMessageId == "d1");
        CountRequests("/v1.0/deltaOld").Should().Be(1); // 断点 URL 被直接请求
        (await provider.CompleteAsync()).Should().Be(newLink);
    }

    [Fact]
    public async Task RemovedItems_MappedToChangeKindRemoved()
    {
        Stub(InitialPath, PageJson(new[]
        {
            "{\"@removed\":\"deleted\",\"id\":\"r1\"}",
            MsgJson("a1"),
        }, deltaLink: Base + "/v1.0/deltaDone"));

        await using var provider = NewProvider();
        var messages = await CollectAsync(provider, null);

        messages.Should().HaveCount(2);
        messages.Single(m => m.ProviderMessageId == "r1").Kind.Should().Be(ChangeKind.Removed);
        messages.Single(m => m.ProviderMessageId == "a1").Kind.Should().Be(ChangeKind.Added);
    }

    [Fact]
    public async Task FieldMapping_ParsesAllSelectedFields()
    {
        Stub(InitialPath, PageJson(new[] { MsgJson("f1", subject: "学费提醒", preview: "请于 9 月 30 日前", address: "finance@hku.hk", hasAttachments: true) },
            deltaLink: Base + "/v1.0/deltaDone"));

        await using var provider = NewProvider();
        var message = (await CollectAsync(provider, null)).Single();

        message.ProviderMessageId.Should().Be("f1");
        message.InternetMessageId.Should().Be("<f1@im>");
        message.Subject.Should().Be("学费提醒");
        message.FromName.Should().Be("Sender");
        message.FromAddress.Should().Be("finance@hku.hk");
        message.ReceivedAtUtc.Should().Be(new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc));
        message.IsRead.Should().BeFalse();
        message.HasAttachments.Should().BeTrue();
        message.BodyPreview.Should().Be("请于 9 月 30 日前");
        message.RemoteChangeKey.Should().Be("ck-f1");
    }

    // ———— EX-TC-01：Graph 持续/瞬时 429 ————

    [Fact]
    public async Task ExTc01_429_RetriesPerRetryAfter_ThenSucceeds()
    {
        // 场景状态机：429 → 429 → 200（Retry-After: 0）
        _server.Given(Request.Create().WithPath(InitialPath).UsingGet())
            .InScenario("throttle").WillSetStateTo("t2")
            .RespondWith(BuildStatusResponse(429, "0"));
        _server.Given(Request.Create().WithPath(InitialPath).UsingGet())
            .InScenario("throttle").WhenStateIs("t2").WillSetStateTo("t3")
            .RespondWith(BuildStatusResponse(429, "0"));
        _server.Given(Request.Create().WithPath(InitialPath).UsingGet())
            .InScenario("throttle").WhenStateIs("t3")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(PageJson(new[] { MsgJson("a1") }, deltaLink: Base + "/v1.0/deltaDone")));

        await using var provider = NewProvider();
        var messages = await CollectAsync(provider, null);

        messages.Should().ContainSingle(m => m.ProviderMessageId == "a1");
        CountRequests(InitialPath).Should().Be(3); // 1 次失败 + 2 次重试
    }

    [Fact]
    public async Task ExTc01_429Exhausted_ThrowsSync002()
    {
        StubStatus(InitialPath, 429, retryAfter: "0");

        await using var provider = NewProvider();
        var act = () => CollectAsync(provider, null);

        (await act.Should().ThrowAsync<MailProviderException>())
            .Where(ex => ex.ErrorCode == "SYNC-002"); // 退避 3 次后本轮终止（EX-03）
        CountRequests(InitialPath).Should().Be(4);    // 1 + 3 次重试
    }

    // ———— EX-TC-02：中途 401，静默刷新续传 / 拒绝刷新则 ReauthRequired ————

    [Fact]
    public async Task ExTc02_401_ForceRefreshesOnce_ThenSucceeds()
    {
        _tokens.RefreshedSilentResult = AuthResult.Ok(Token("tok-2"));
        _server.Given(Request.Create().WithPath(InitialPath).UsingGet())
            .InScenario("auth").WillSetStateTo("ok")
            .RespondWith(BuildStatusResponse(401, null));
        _server.Given(Request.Create().WithPath(InitialPath).UsingGet())
            .InScenario("auth").WhenStateIs("ok")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(PageJson(new[] { MsgJson("a1") }, deltaLink: Base + "/v1.0/deltaDone")));

        await using var provider = NewProvider();
        var messages = await CollectAsync(provider, null);

        messages.Should().ContainSingle(m => m.ProviderMessageId == "a1");
        _tokens.ForceRefreshedCalls.Should().Be(1); // 401 → 强制刷新一次后重试成功（EX-TC-02 上半）
        CountRequests(InitialPath).Should().Be(2);
    }

    [Fact]
    public async Task ExTc02_401_AfterRefreshStill401_ThrowsAuth003()
    {
        _tokens.RefreshedSilentResult = AuthResult.Ok(Token("tok-2"));
        StubStatus(InitialPath, 401);

        await using var provider = NewProvider();
        var act = () => CollectAsync(provider, null);

        (await act.Should().ThrowAsync<MailProviderException>())
            .Where(ex => ex.ErrorCode == "AUTH-003"); // 拒绝刷新 → ReauthRequired（EX-TC-02 下半）
    }

    // ———— EX-TC-03：deltaLink 失效（410）自动全量重同步 ————

    [Fact]
    public async Task ExTc03_DeltaLinkExpired_FallsBackToFullSync()
    {
        StubStatus("/v1.0/deltaOld", 410);
        var newDelta = Base + "/v1.0/deltaNew?token=n1";
        Stub(InitialPath, PageJson(new[] { MsgJson("full1"), MsgJson("full2") }, deltaLink: newDelta));

        await using var provider = NewProvider();
        var messages = await CollectAsync(provider, Base + "/v1.0/deltaOld?token=old");

        messages.Should().HaveCount(2); // 410 → 自动降级全量（SYNC-003）
        (await provider.CompleteAsync()).Should().Be(newDelta);
        CountRequests(InitialPath).Should().Be(1);
    }

    // ———— EX-TC-04：网络不可达 → SYNC-001 Offline ————

    [Fact]
    public async Task ExTc04_ConnectionRefused_MapsToSync001()
    {
        // 动态空闲端口（无人监听→必然 ECONNREFUSED）；固定低位端口（如 :1）可能被防火墙 DROP 伪装成超时
        var port = GetFreePort();
        await using var provider = NewProvider(baseAddress: new Uri($"http://127.0.0.1:{port}/"));
        var act = () => CollectAsync(provider, null);

        (await act.Should().ThrowAsync<MailProviderException>())
            .Where(ex => ex.ErrorCode == "SYNC-001");
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    // ———— 04 §4.1 例外：P0 候选同步时即拉正文 ————

    [Fact]
    public async Task P0Candidate_FetchesBodyIntoCache()
    {
        Stub(InitialPath, PageJson(new[] { MsgJson("p0", subject: "TUITION FINAL REMINDER"), MsgJson("normal", subject: "Weekly digest") },
            deltaLink: Base + "/v1.0/deltaDone"));
        Stub("/v1.0/me/messages/p0", "{\"body\":{\"contentType\":\"html\",\"content\":\"<html>fee due</html>\"}}");
        var cache = new BodyCacheStore(Path.Combine(_dir, "bodies"));

        await using var provider = NewProvider(cache, m => m.Subject.Contains("TUITION"));
        var messages = await CollectAsync(provider, null);

        var p0 = messages.Single(m => m.ProviderMessageId == "p0");
        var normal = messages.Single(m => m.ProviderMessageId == "normal");
        p0.HtmlPath.Should().NotBeNull(); // 候选拉正文并写缓存
        (await cache.ReadAsync(p0.HtmlPath!, CancellationToken.None)).Should().Be("<html>fee due</html>");
        normal.HtmlPath.Should().BeNull(); // 非候选不拉（提速）
        CountRequests("/v1.0/me/messages/p0").Should().Be(1);
    }

    [Fact]
    public async Task TestAsync_ChecksMeEndpoint()
    {
        Stub("/v1.0/me", "{\"userPrincipalName\":\"s123456@connect.hku.hk\"}");

        await using var provider = NewProvider();
        var result = await provider.TestAsync();

        result.IsSuccess.Should().BeTrue(); // G-1 登录后校验账户
        result.Message.Should().Contain("s123456@connect.hku.hk");
    }
}
