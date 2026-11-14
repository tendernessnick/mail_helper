using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Cryptography;
using System.Collections.Concurrent;
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
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/tendernessnick/mail_helper/releases/latest";

    /// <summary>更新资产名（MS9 平台化）：Win=Inno Setup.exe（S17 名不变）；mac=对应架构 DMG（docs/10 §9）。</summary>
    public static string InstallerAssetName =>
        OperatingSystem.IsMacOS()
            ? $"MailHelper-stable-osx-{(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64")}.dmg"
            : "MailHelper-stable-Setup.exe";

    private static readonly HttpClient Http = CreateClient();
    private readonly IUpdateInstaller _installer;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, string> _verifiedPackages = new(StringComparer.Ordinal);

    /// <summary>installer 为平台策略必选注入（DI 注册 WindowsUpdateInstaller；macOS 由 Avalonia 侧提供，docs/10 §9）。</summary>
    public UpdateService(IUpdateInstaller installer, HttpClient? httpClient = null)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _http = httpClient ?? Http;
    }

    /// <summary>当前版本（AssemblyVersion，CI 以 tag 注入 -p:Version）。</summary>
    public static string CurrentVersion { get; } =
        (typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    /// <summary>一条可下载的新版本记录。</summary>
    public sealed record PendingUpdate(string Version, string DownloadUrl, long SizeBytes, string Sha256);

    /// <summary>检查更新：无更新返回 null。</summary>
    public async Task<PendingUpdate?> CheckForUpdateAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.UserAgent.ParseAdd("MailHelper-Update");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        if (json.RootElement.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || json.RootElement.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
        {
            return null;
        }

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
                var hash = await GetChecksumAsync(json.RootElement, asset, ct).ConfigureAwait(false);
                var normalized = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
                return new PendingUpdate(normalized.ToString(3), url, size, hash);
            }
        }

        return null;
    }

    /// <summary>下载安装包到临时目录（progress：已读字节 / 总字节），返回本地路径。</summary>
    public async Task<string> DownloadUpdateAsync(
        PendingUpdate update, Action<long, long>? progress, CancellationToken ct)
    {
        if (!IsSha256(update.Sha256))
        {
            throw new InvalidDataException("更新包缺少有效 SHA256 校验值。");
        }

        using var resp = await _http.GetAsync(
            update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? update.SizeBytes;
        if (update.SizeBytes > 0 && total != update.SizeBytes)
        {
            throw new InvalidDataException("更新包大小与发布记录不一致。");
        }

        // 每次独立目录 + .partial，失败/取消清理，完整验证后才暴露可安装的文件名。
        var directory = Path.Combine(Path.GetTempPath(), "MailHelper-updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var partial = Path.Combine(directory, InstallerAssetName + ".partial");
        var target = Path.Combine(directory, InstallerAssetName);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(
                partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, n);
                    read += n;
                    if (total > 0 && read > total)
                    {
                        throw new InvalidDataException("更新包大小与发布记录不一致。");
                    }

                    progress?.Invoke(read, total);
                }

                if (read == 0 || total > 0 && read != total
                    || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("更新包不完整或 SHA256 校验失败，请重新下载。");
                }
            }

            ct.ThrowIfCancellationRequested();
            File.Move(partial, target);
            _verifiedPackages[target] = update.Sha256;
            return target;
        }
        catch
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>启动静默升级（IUpdateInstaller 策略，Windows=Inno /SILENT 同目录覆盖安装）；
    /// 调用方随后退出应用，安装完成后由安装器自动重启 MailHelper。</summary>
    public void InstallSilently(string installerPath)
    {
        if (!_verifiedPackages.TryGetValue(installerPath, out var expectedHash))
        {
            throw new InvalidDataException("仅可安装已下载并通过校验的更新包。");
        }

        using var file = File.OpenRead(installerPath);
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包已改变，请重新下载。");
        }

        _installer.Install(installerPath);
    }

    private async Task<string> GetChecksumAsync(JsonElement release, JsonElement asset, CancellationToken ct)
    {
        if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } value
            && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && IsSha256(value[7..]))
        {
            return value[7..];
        }

        foreach (var checksumAsset in release.GetProperty("assets").EnumerateArray())
        {
            if (checksumAsset.GetProperty("name").GetString() != "SHA256SUMS.txt"
                || checksumAsset.GetProperty("browser_download_url").GetString() is not { } url)
            {
                continue;
            }

            var sums = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
            foreach (var line in sums.Split('\n'))
            {
                var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1].TrimStart('*').Trim() == InstallerAssetName && IsSha256(parts[0]))
                {
                    return parts[0];
                }
            }
        }

        throw new InvalidDataException("发布记录缺少安装包 SHA256 校验值，请稍后重试。");
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MailHelper-Update");
        return client;
    }
}
