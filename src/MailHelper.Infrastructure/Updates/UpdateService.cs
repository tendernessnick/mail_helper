using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using MailHelper.Core.Abstractions;
using MailHelper.Infrastructure.SystemIntegration;

namespace MailHelper.Infrastructure.Updates;

/// <summary>应用内更新（S17，Inno 安装包路线；MS2 自 App 迁入 Infrastructure 供共享 ViewModel 消费）：更新源为 GitHub Releases。
/// 检查 = releases/latest 与当前程序集版本比较（只认正式版，预发布不推送）；
/// 下载 = 安装包落临时目录（字节级进度）；应用 = IUpdateInstaller 策略
/// （MS1 抽取：Windows=WindowsUpdateInstaller 静默重装；macOS 由 Avalonia 侧提供引导安装，docs/10 §9）。</summary>
public sealed class UpdateService
{
    public const string RepoUrl = "https://github.com/tendernessnick/mail_helper";
    public const string InstallerAssetName = "MailHelper-stable-Setup.exe";

    private static readonly HttpClient Http = CreateClient();
    private readonly IUpdateInstaller _installer;

    /// <summary>installer 为平台策略必选注入（DI 注册 WindowsUpdateInstaller；macOS 由 Avalonia 侧提供，docs/10 §9）。</summary>
    public UpdateService(IUpdateInstaller installer) =>
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));

    /// <summary>当前版本（AssemblyVersion，CI 以 tag 注入 -p:Version）。</summary>
    public static string CurrentVersion { get; } =
        (typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    /// <summary>一条可下载的新版本记录。</summary>
    public sealed record PendingUpdate(string Version, string DownloadUrl, long SizeBytes);

    /// <summary>检查更新：无更新返回 null。</summary>
    public async Task<PendingUpdate?> CheckForUpdateAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, RepoUrl + "/releases/latest");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var tag = json.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) ||
            latest <= Version.Parse(CurrentVersion))
        {
            return null;
        }

        foreach (var asset in json.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != InstallerAssetName)
            {
                continue;
            }

            var url = asset.GetProperty("browser_download_url").GetString();
            var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            if (url is not null)
            {
                return new PendingUpdate(latest.ToString(3), url, size);
            }
        }

        return null;
    }

    /// <summary>下载安装包到临时目录（progress：已读字节 / 总字节），返回本地路径。</summary>
    public async Task<string> DownloadUpdateAsync(
        PendingUpdate update, Action<long, long>? progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(
            update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? update.SizeBytes;

        var target = Path.Combine(Path.GetTempPath(), InstallerAssetName);
        await using var http = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = new FileStream(
            target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

        var buffer = new byte[1 << 16];
        long read = 0;
        int n;
        while ((n = await http.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            progress?.Invoke(read, total);
        }

        return target;
    }

    /// <summary>启动静默升级（IUpdateInstaller 策略，Windows=Inno /SILENT 同目录覆盖安装）；
    /// 调用方随后退出应用，安装完成后由安装器自动重启 MailHelper。</summary>
    public void InstallSilently(string installerPath) => _installer.Install(installerPath);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MailHelper-Update");
        return client;
    }
}
