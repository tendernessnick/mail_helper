using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Infrastructure.SystemIntegration;
using Xunit;

namespace MailHelper.Mac.Tests;

/// <summary>IAppPaths/ISingleInstanceLock/IAutoStarter Mac 实现契约（docs/10 §12.2；与 Windows 契约组同形镜像：
/// WindowsAppPathsTests/WindowsSingleInstanceLockTests）。实现为纯托管路径构造/AF_UNIX/plist 文本，
/// 全平台可跑；真实 macOS 行为由 CI macos job 真机执行兜底。</summary>
public class MacPlatformContractTests
{
    // —— IAppPaths（P-02）——

    [Fact]
    public void AppPaths_Default_IsLibraryApplicationSupport()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "MailHelper");
        new MacAppPaths().DataDir.Should().Be(expected);
    }

    [Fact]
    public void AppPaths_Override_RespectedForDerivedPaths()
    {
        var paths = new MacAppPaths("/tmp/mh-mac-test");
        paths.DataDir.Should().Be("/tmp/mh-mac-test");
        Path.GetFileName(paths.LogsDir).Should().Be("logs");
        Path.GetFileName(paths.BodiesDir).Should().Be("bodies");
        Path.GetFileName(paths.DbPath).Should().Be("mailhelper.db");
        paths.GetTempFilePath("probe.txt").Should().EndWith("probe.txt");
    }

    [Fact]
    public void AppPaths_SocketFile_LivesInDataDir()
    {
        // 单实例 socket 落数据目录 → MAILHELPER_DATA_DIR 隔离即测试隔离（docs/10 §8.2）
        var paths = new MacAppPaths("/tmp/mh-mac-test");
        MacSingleInstanceLock.DefaultSocketPath(paths.DataDir)
            .Should().Be(Path.Combine("/tmp/mh-mac-test", "instance.sock"));
    }

    // —— ISingleInstanceLock（P-03，UDS 文件锁；语义镜像 Windows 组）——

    private static (string SocketDir, string SocketPath) UniqueSocket()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mh-uds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, Path.Combine(dir, "instance.sock"));
    }

    [Fact]
    public void Lock_FirstAcquire_Succeeds_SecondFails_DisposeReleases()
    {
        var (dir, socket) = UniqueSocket();
        var first = new MacSingleInstanceLock(socket);
        using (first)
        {
            first.TryAcquireFirst().Should().BeTrue();
            first.StartListening(() => { }); // 真实用法：App 在 TryAcquireFirst 后立即 StartListening

            // 跨线程模拟第二实例（与 Windows 组同因：避免同线程语义干扰）
            using var second = new MacSingleInstanceLock(socket);
            bool? secondResult = null;
            var contender = new Thread(() => secondResult = second.TryAcquireFirst());
            contender.Start();
            contender.Join();
            secondResult.Should().BeFalse("socket 已被首实例 bind 且可连（存活实例）");
        }

        using var third = new MacSingleInstanceLock(socket);
        third.TryAcquireFirst().Should().BeTrue("Dispose 后 socket 释放且文件清理");
    }

    [Fact]
    public void Lock_StaleSocketFile_IsCleanedUp()
    {
        // 崩溃残留：socket 文件存在但无监听者（bind 后立即关闭即残留文件）→ 清理后可成为首实例
        var (dir, socket) = UniqueSocket();
        var stale = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        stale.Bind(new UnixDomainSocketEndPoint(socket));
        stale.Dispose(); // 不删文件、不监听 = 残留态

        using var fresh = new MacSingleInstanceLock(socket);
        fresh.TryAcquireFirst().Should().BeTrue("连接被拒的残留 socket 应被清理");
    }

    [Fact]
    public async Task Lock_StartListening_InvokesActivate_OnShowSignal()
    {
        var (dir, socket) = UniqueSocket();
        var first = new MacSingleInstanceLock(socket);
        using (first)
        {
            first.TryAcquireFirst().Should().BeTrue();

            var activated = new TaskCompletionSource();
            first.StartListening(activated.SetResult);

            using var second = new MacSingleInstanceLock(socket);
            second.NotifyRunningInstance();

            var done = await Task.WhenAny(activated.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            done.Should().Be(activated.Task, "二次启动的 SHOW 应唤起首实例回调");
        }
    }

    // —— IAutoStarter（P-04，LaunchAgent）——

    [Fact]
    public void AutoStart_PlistBuilder_EmitsRunAtLoadAgent()
    {
        var plist = LaunchAgentAutoStart.BuildPlist(
            "/Applications/MailHelper.app/Contents/MacOS/MailHelper.App.Avalonia");
        plist.Should().Contain("<key>Label</key>");
        plist.Should().Contain(LaunchAgentAutoStart.Label);
        plist.Should().Contain("<key>RunAtLoad</key>");
        plist.Should().Contain("<true/>");
        plist.Should().Contain("/Applications/MailHelper.app");
    }

    [Fact]
    public void AutoStart_EnableDisable_WritesAndRemovesPlist_ViaFakeLauncher()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mh-autostart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var launcherCalls = new List<string>();
        var autostart = new LaunchAgentAutoStart(
            agentsDir: dir,
            executablePath: "/Applications/MailHelper.app/Contents/MacOS/MailHelper.App.Avalonia",
            launchctl: args => { launcherCalls.Add(string.Join(' ', args)); return 0; });

        autostart.Enable();
        File.Exists(Path.Combine(dir, LaunchAgentAutoStart.Label + ".plist")).Should().BeTrue();
        autostart.IsEnabled.Should().BeTrue();
        launcherCalls.Should().Contain(args => args.Contains("bootstrap"));

        autostart.Disable();
        File.Exists(Path.Combine(dir, LaunchAgentAutoStart.Label + ".plist")).Should().BeFalse();
        autostart.IsEnabled.Should().BeFalse();
        launcherCalls.Should().Contain(args => args.Contains("bootout"));
    }

    [Fact]
    public void AutoStart_DefaultCtor_TargetsUserLaunchAgentsDir()
    {
        var autostart = new LaunchAgentAutoStart(
            executablePath: "/Applications/MailHelper.app/Contents/MacOS/MailHelper.App.Avalonia",
            launchctl: _ => 0);
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
        Path.GetDirectoryName(Path.Combine(autostart.PlistPath)).Should().Be(expected);
    }
}
