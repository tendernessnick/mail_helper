using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Infrastructure.Updates;
using Xunit;

namespace MailHelper.Services.Tests;

public class UpdateServiceTests
{
    private const string DownloadUrl = "https://github.com/tendernessnick/mail_helper/releases/download/v99.1.0/package";
    private const string ChecksumUrl = "https://github.com/tendernessnick/mail_helper/releases/download/v99.1.0/SHA256SUMS.txt";
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("synthetic installer bytes; never execute");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Payload));

    [Fact]
    public async Task Check_UsesGitHubApiAndHeaders_AndReadsAssetDigest()
    {
        using var client = new HttpClient(new Handler(request =>
        {
            request.RequestUri!.AbsoluteUri.Should().Be(UpdateService.LatestReleaseApiUrl);
            request.Headers.Accept.Should().Contain(h => h.MediaType == "application/vnd.github+json");
            request.Headers.UserAgent.ToString().Should().Contain("MailHelper");
            return ReleaseResponse(digest: "sha256:" + Hash);
        }));
        var service = new UpdateService(new Installer(), client);

        var result = await service.CheckForUpdateAsync(CancellationToken.None);

        result.Should().Be(new UpdateService.PendingUpdate("99.1.0", DownloadUrl, Payload.Length, Hash));
    }

    [Fact]
    public async Task Check_OlderRelease_ReadsPublishedChecksumManifest()
    {
        using var client = new HttpClient(new Handler(request =>
            request.RequestUri!.AbsoluteUri == UpdateService.LatestReleaseApiUrl
                ? ReleaseResponse(includeManifest: true)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{new string('0', 64)}  other.zip\n{Hash.ToLowerInvariant()}  {UpdateService.InstallerAssetName}\n"),
                }));

        var result = await new UpdateService(new Installer(), client).CheckForUpdateAsync(CancellationToken.None);

        result!.Sha256.Should().Be(Hash.ToLowerInvariant());
    }

    [Theory]
    [InlineData("0.0.1", false)]
    [InlineData("99.1.0", true)]
    public async Task Check_OldOrPrerelease_DoesNotOfferUpdate(string version, bool prerelease)
    {
        using var client = new HttpClient(new Handler(_ => ReleaseResponse(version: version, prerelease: prerelease)));
        (await new UpdateService(new Installer(), client).CheckForUpdateAsync(CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task Check_MissingChecksum_DoesNotOfferUnverifiableInstaller()
    {
        using var client = new HttpClient(new Handler(_ => ReleaseResponse()));
        var action = () => new UpdateService(new Installer(), client).CheckForUpdateAsync(CancellationToken.None);
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Download_VerifiesBytes_ReportsProgress_UsesUniquePaths_AndAllowsInstall()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Payload),
        }));
        var installer = new Installer();
        var service = new UpdateService(installer, client);
        var update = new UpdateService.PendingUpdate("99.1.0", DownloadUrl, Payload.Length, Hash);
        var progress = new List<(long Read, long Total)>();
        var first = await service.DownloadUpdateAsync(update, (read, total) => progress.Add((read, total)), CancellationToken.None);
        var second = await service.DownloadUpdateAsync(update, null, CancellationToken.None);
        try
        {
            first.Should().NotBe(second);
            File.ReadAllBytes(first).Should().Equal(Payload);
            Directory.GetFiles(Path.GetDirectoryName(first)!).Should().Equal(first);
            progress.Last().Should().Be(((long)Payload.Length, (long)Payload.Length));
            service.InstallSilently(first);
            installer.Installed.Should().Equal(first);
            File.WriteAllText(second, "tampered after download");
            var installTampered = () => service.InstallSilently(second);
            installTampered.Should().Throw<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(first)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(second)!, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Download_CorruptOrTruncatedBytes_AreRejected(bool wrongHash)
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Payload),
        }));
        var update = new UpdateService.PendingUpdate("99.1.0", DownloadUrl,
            wrongHash ? Payload.Length : Payload.Length + 1, wrongHash ? new string('0', 64) : Hash);
        var action = () => new UpdateService(new Installer(), client).DownloadUpdateAsync(update, null, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Download_CancelDuringTransfer_RejectsPartialInstaller()
    {
        using var cts = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Payload),
        }));
        var service = new UpdateService(new Installer(), client);
        var update = new UpdateService.PendingUpdate("99.1.0", DownloadUrl, Payload.Length, Hash);
        var action = () => service.DownloadUpdateAsync(update, (_, _) => cts.Cancel(), cts.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Install_UnknownFile_IsRejectedBeforeLaunchingInstaller()
    {
        var installer = new Installer();
        var service = new UpdateService(installer);
        var action = () => service.InstallSilently("arbitrary.exe");
        action.Should().Throw<InvalidDataException>();
        installer.Installed.Should().BeEmpty();
    }

    private static HttpResponseMessage ReleaseResponse(
        string version = "99.1.0", string? digest = null, bool includeManifest = false, bool prerelease = false)
    {
        var assets = new List<object>
        {
            new { name = UpdateService.InstallerAssetName, browser_download_url = DownloadUrl, size = Payload.Length, digest },
        };
        if (includeManifest)
        {
            assets.Add(new { name = "SHA256SUMS.txt", browser_download_url = ChecksumUrl });
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { tag_name = "v" + version, draft = false, prerelease, assets })),
        };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class Installer : IUpdateInstaller
    {
        public List<string> Installed { get; } = [];
        public void Install(string localPackagePath) => Installed.Add(localPackagePath);
    }
}
