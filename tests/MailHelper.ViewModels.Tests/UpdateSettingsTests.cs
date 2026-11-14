using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Updates;
using MailHelper.ViewModels;
using Xunit;

namespace MailHelper.ViewModels.Tests;

/// <summary>真实更新服务 + 假 HTTP/安装器，覆盖“已就绪 → 安装”命令的失败恢复；不启动任何安装程序。</summary>
public sealed class UpdateSettingsTests : IDisposable
{
    private readonly string _payloadText = "synthetic MailHelper VM update " + Guid.NewGuid().ToString("N");
    private readonly Installer _installer = new();
    private readonly List<(string Stage, Exception? Error)> _errors = [];
    private readonly HttpClient _http;
    private readonly SettingsViewModel _vm;
    private int _shutdowns;
    private string? _downloadedPath;

    public UpdateSettingsTests()
    {
        var payload = Encoding.UTF8.GetBytes(_payloadText);
        var digest = Convert.ToHexString(SHA256.HashData(payload));
        _http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == UpdateService.LatestReleaseApiUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        tag_name = "v99.1.0",
                        assets = new[]
                        {
                            new
                            {
                                name = UpdateService.InstallerAssetName,
                                browser_download_url = "https://github.com/tendernessnick/mail_helper/releases/download/v99.1.0/synthetic",
                                size = payload.Length,
                                digest = "sha256:" + digest,
                            },
                        },
                    })),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }));
        _vm = new SettingsViewModel(new SettingsService(new Settings()), new Accounts(), _ => { },
            new UpdateService(_installer, _http), writeCrashLog: (stage, error) => _errors.Add((stage, error)))
        {
            CanCheckUpdate = true,
            ShutdownCallback = () => _shutdowns++,
        };
    }

    private async Task PrepareUpdateAsync()
    {
        await _vm.CheckUpdateCommand.ExecuteAsync(null);
        _vm.UpdateReady.Should().BeTrue();
        var root = Path.Combine(Path.GetTempPath(), "MailHelper-updates");
        _downloadedPath = Directory.EnumerateFiles(root, UpdateService.InstallerAssetName, SearchOption.AllDirectories)
            .Where(path => new FileInfo(path).Length == Encoding.UTF8.GetByteCount(_payloadText))
            .Single(path => File.ReadAllText(path) == _payloadText);
    }

    [Fact]
    public async Task Apply_ValidDownloadedPackage_StartsInstallerBeforeShutdown()
    {
        await PrepareUpdateAsync();
        _installer.OnInstall = _ => _shutdowns.Should().Be(0, "安装器启动前不能退出应用");

        _vm.ApplyUpdateCommand.Execute(null);

        _installer.Calls.Should().Be(1);
        _shutdowns.Should().Be(1);
        _vm.UpdateReady.Should().BeFalse();
        _errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_DeletedPackage_ShowsRecoveryMessage_AndKeepsApplicationOpen()
    {
        await PrepareUpdateAsync();
        File.Delete(_downloadedPath!);

        _vm.ApplyUpdateCommand.Execute(null);

        _vm.UpdateReady.Should().BeFalse();
        _vm.UpdateStatus.Should().Contain("已丢失").And.Contain("重新检查更新");
        _installer.Calls.Should().Be(0);
        _shutdowns.Should().Be(0);
    }

    [Fact]
    public async Task Apply_ModifiedPackage_ShowsValidationFailure_AndCanDownloadAgain()
    {
        await PrepareUpdateAsync();
        var firstPath = _downloadedPath!;
        File.WriteAllText(firstPath, "modified after download");
        try
        {
            _vm.ApplyUpdateCommand.Execute(null);

            _vm.UpdateReady.Should().BeFalse();
            _vm.UpdateStatus.Should().Contain("校验失败").And.Contain("重新检查更新");
            _installer.Calls.Should().Be(0);
            _shutdowns.Should().Be(0);
            _errors.Should().ContainSingle(e => e.Stage == "ApplyUpdate" && e.Error is InvalidDataException);
            await PrepareUpdateAsync();
            _vm.ApplyUpdateCommand.Execute(null);
            _shutdowns.Should().Be(1, "失败后重新下载仍可完成更新");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(firstPath)!, recursive: true);
        }
    }

    [Fact]
    public async Task Apply_InstallerLaunchFails_ReportsErrorAndDoesNotExit()
    {
        await PrepareUpdateAsync();
        _installer.OnInstall = _ => throw new Win32Exception("synthetic launch failure");

        _vm.ApplyUpdateCommand.Execute(null);

        _vm.UpdateReady.Should().BeFalse();
        _vm.UpdateStatus.Should().Contain("无法启动").And.Contain("重试");
        _shutdowns.Should().Be(0);
        _errors.Should().ContainSingle(e => e.Stage == "ApplyUpdate" && e.Error is Win32Exception);
    }

    [Fact]
    public void Apply_StaleReadyStateWithoutDownload_ResetsStateAndKeepsApplicationOpen()
    {
        _vm.UpdateReady = true;

        _vm.ApplyUpdateCommand.Execute(null);

        _vm.UpdateReady.Should().BeFalse();
        _vm.UpdateStatus.Should().Contain("重新检查更新");
        _installer.Calls.Should().Be(0);
        _shutdowns.Should().Be(0);
    }

    public void Dispose()
    {
        _http.Dispose();
        if (_downloadedPath is { } path && Directory.Exists(Path.GetDirectoryName(path)))
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class Installer : IUpdateInstaller
    {
        public int Calls { get; private set; }
        public Action<string>? OnInstall { get; set; }
        public void Install(string localPackagePath)
        {
            Calls++;
            OnInstall?.Invoke(localPackagePath);
        }
    }

    private sealed class Settings : ISettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Accounts : IAccountStore
    {
        public Task UpsertAccountAsync(Account account, CancellationToken ct) => Task.CompletedTask;
        public Task<Account?> FindAccountAsync(string accountId, CancellationToken ct) => Task.FromResult<Account?>(null);
        public Task<IReadOnlyList<Account>> FindAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Account>>([]);
        public Task<SyncCheckpoint?> GetCheckpointAsync(string accountId, CancellationToken ct) => Task.FromResult<SyncCheckpoint?>(null);
        public Task SaveCheckpointAsync(SyncCheckpoint checkpoint, CancellationToken ct) => Task.CompletedTask;
    }
}
