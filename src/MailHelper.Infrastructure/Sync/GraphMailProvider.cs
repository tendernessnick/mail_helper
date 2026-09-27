using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Infrastructure.Sync;

/// <summary>Graph REST v1.0 邮件通道（04 章 §4.1 调用清单 G-1/G-3/G-4；CHG-006：HttpClient 直调）。
/// delta query 分页与断点续传、@removed 删除语义、P0 候选单封拉正文（04 §4.1 例外）、
/// 429 Retry-After / 5xx 指数退避（2s/8s/30s，最多 3 次重试）、401 强制刷新一次（04 §4.2）、
/// deltaLink 410 自动降级全量（SYNC-003 / EX-TC-03）。</summary>
public sealed class GraphMailProvider : IMailProvider
{
    private const string SelectFields = "id,internetMessageId,subject,from,receivedDateTime,isRead,hasAttachments,bodyPreview,changeKey";

    private readonly ITokenProvider _tokenProvider;
    private readonly Uri _graphBase;
    private readonly GraphHttpOptions _options;
    private readonly HttpClient _http;
    private readonly IBodyCache? _bodyCache;
    private readonly Func<RemoteMessage, bool> _isP0Candidate;
    private readonly string _cacheAccountKey;
    private readonly bool _ownsHttpClient;
    private string? _newDeltaLink;
    private bool _refreshedOnce;

    public GraphMailProvider(
        ITokenProvider tokenProvider,
        Uri? graphBaseAddress = null,
        GraphHttpOptions? options = null,
        IBodyCache? bodyCache = null,
        Func<RemoteMessage, bool>? p0CandidateDetector = null,
        HttpClient? httpClient = null,
        string? cacheAccountKey = null)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _graphBase = graphBaseAddress ?? new Uri("https://graph.microsoft.com/");
        _options = options ?? new GraphHttpOptions();
        _bodyCache = bodyCache;
        _isP0Candidate = p0CandidateDetector ?? DefaultP0CandidateDetector;
        _cacheAccountKey = cacheAccountKey ?? "graph";
        _http = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public ChannelKind Kind => ChannelKind.Graph;

    public ValueTask DisposeAsync()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<RemoteMessage> FetchDeltaAsync(string? deltaLink, int pageSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _newDeltaLink = null;
        _refreshedOnce = false;
        var url = deltaLink ?? BuildInitialUrl(pageSize);
        var restartedForExpiredLink = false;

        while (true)
        {
            JsonDocument page;
            try
            {
                page = await SendWithPolicyAsync(url, ct);
            }
            catch (DeltaLinkExpiredException) when (!restartedForExpiredLink)
            {
                restartedForExpiredLink = true;
                url = BuildInitialUrl(pageSize); // SYNC-003：deltaLink 失效 → 自动全量重同步
                continue;
            }

            using (page)
            {
                foreach (var message in ParsePage(page))
                {
                    var current = message;
                    if (_bodyCache is not null && current.Kind != ChangeKind.Removed && _isP0Candidate(current))
                    {
                        current = await WithBodyCachedAsync(current, ct);
                    }

                    yield return current;
                }

                var root = page.RootElement;
                if (root.TryGetProperty("@odata.nextLink", out var nextLink))
                {
                    url = nextLink.GetString() ?? url;
                    continue;
                }

                if (root.TryGetProperty("@odata.deltaLink", out var deltaOut))
                {
                    _newDeltaLink = deltaOut.GetString();
                }

                yield break;
            }
        }
    }

    /// <summary>返回本轮同步的新 deltaLink；未完成/失败返回 null（断点保持旧值，EX-05）。</summary>
    public Task<string?> CompleteAsync() => Task.FromResult(_newDeltaLink);

    public async Task<ConnectionTestResult> TestAsync()
    {
        try
        {
            using var doc = await SendWithPolicyAsync($"{_graphBase}v1.0/me?$select=id,userPrincipalName", CancellationToken.None);
            var email = GetString(doc.RootElement, "userPrincipalName");
            return new ConnectionTestResult(true, Message: email);
        }
        catch (MailProviderException ex)
        {
            return ConnectionTestResult.Fail(ex.ErrorCode, ex.Message);
        }
    }

    private string BuildInitialUrl(int pageSize) =>
        // D-33：Inbox 用 well-known 别名（G-2 省一次往返）；$select 不含 body（全量提速，04 §4.1）
        $"{_graphBase}v1.0/me/mailFolders/inbox/messages/delta?$select={SelectFields}&$top={pageSize}";

    private async Task<JsonDocument> SendWithPolicyAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var auth = await _tokenProvider.AcquireTokenSilentAsync(_refreshedOnce, ct);
            if (!auth.IsSuccess || auth.Token is null)
            {
                throw new MailProviderException("AUTH-003", auth.Message ?? "令牌获取失败（需重新登录）");
            }

            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token.AccessToken);
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException)
            {
                if (attempt >= _options.MaxRetries)
                {
                    throw new MailProviderException("SYNC-001", "网络不可达（退避重试耗尽）", ex);
                }

                await Task.Delay(_options.BackoffDelays[Math.Min(attempt, _options.BackoffDelays.Count - 1)], ct);
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    return JsonDocument.Parse(json);
                }

                var status = (int)response.StatusCode;
                if (status == 401)
                {
                    if (!_refreshedOnce)
                    {
                        _refreshedOnce = true; // 401 → 强制刷新一次后重试（04 §4.2）
                        attempt--;             // 刷新重试不消耗退避次数
                        continue;
                    }

                    throw new MailProviderException("AUTH-003", "令牌刷新后仍被拒绝（需重新登录）");
                }

                if (status == 410)
                {
                    throw new DeltaLinkExpiredException();
                }

                if (status != 429 && status < 500)
                {
                    throw new MailProviderException("SYNC-002", $"Graph 返回 HTTP {status}"); // D-34
                }

                if (attempt >= _options.MaxRetries)
                {
                    throw new MailProviderException(
                        "SYNC-002", status == 429 ? "429 限流退避耗尽" : $"HTTP {status} 退避耗尽"); // SYNC-002 / EX-03
                }

                await Task.Delay(ResolveRetryDelay(response, attempt), ct);
            }
        }
    }

    private TimeSpan ResolveRetryDelay(HttpResponseMessage response, int attempt)
    {
        if ((int)response.StatusCode == 429
            && response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (raw is not null && int.TryParse(raw, out var seconds) && seconds >= 0)
            {
                return TimeSpan.FromSeconds(Math.Min(seconds, 30)); // 429 优先按 Retry-After（04 §4.2）
            }
        }

        return _options.BackoffDelays[Math.Min(attempt, _options.BackoffDelays.Count - 1)];
    }

    private async Task<RemoteMessage> WithBodyCachedAsync(RemoteMessage message, CancellationToken ct)
    {
        try
        {
            var url = $"{_graphBase}v1.0/me/messages/{Uri.EscapeDataString(message.ProviderMessageId)}?$select=body";
            using var doc = await SendWithPolicyAsync(url, ct);
            var content = doc.RootElement.TryGetProperty("body", out var body)
                && body.TryGetProperty("content", out var contentEl)
                && contentEl.ValueKind == JsonValueKind.String
                ? contentEl.GetString()
                : null;
            if (content is null || _bodyCache is null)
            {
                return message;
            }

            if (content.Length > _options.MaxBodyBytes) // EX-06：超大正文截断
            {
                content = content[..(int)_options.MaxBodyBytes];
            }

            var relativePath = await _bodyCache.SaveAsync(_cacheAccountKey, content, ct);
            return message with { HtmlPath = relativePath };
        }
        catch (MailProviderException)
        {
            return message; // 正文预拉失败不阻断同步批次
        }
    }

    private static IEnumerable<RemoteMessage> ParsePage(JsonDocument page)
    {
        if (!page.RootElement.TryGetProperty("value", out var values))
        {
            yield break;
        }

        foreach (var item in values.EnumerateArray())
        {
            var id = GetString(item, "id") ?? string.Empty;
            if (item.TryGetProperty("@removed", out _))
            {
                // 删除事件仅携带 id（D-37）：received 落 1970 占位，入库只置 is_deleted_remote
                yield return new RemoteMessage(id, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, null, DateTime.MinValue, false, false, ChangeKind.Removed);
                continue;
            }

            yield return new RemoteMessage(
                id,
                GetString(item, "internetMessageId") ?? string.Empty,
                GetString(item, "subject") ?? string.Empty,
                GetFromPart(item, "name"),
                GetFromPart(item, "address"),
                GetString(item, "bodyPreview") ?? string.Empty,
                null,
                GetReceivedUtc(item),
                GetBool(item, "hasAttachments"),
                GetBool(item, "isRead"),
                ChangeKind.Added,
                GetString(item, "changeKey"));
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string GetFromPart(JsonElement item, string part) =>
        item.TryGetProperty("from", out var from)
            && from.TryGetProperty("emailAddress", out var emailAddress)
            && GetString(emailAddress, part) is { Length: > 0 } value
            ? value
            : string.Empty;

    private static DateTime GetReceivedUtc(JsonElement item) =>
        GetString(item, "receivedDateTime") is { } raw
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : DateTime.MinValue;

    private static bool DefaultP0CandidateDetector(RemoteMessage message) => false;

    private sealed class DeltaLinkExpiredException : Exception
    {
    }
}
