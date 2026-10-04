using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Threading;
using FluentAssertions;
using MailHelper.App.Avalonia;
using MailHelper.ViewModels;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MailHelper.App.Avalonia.Tests;

/// <summary>三栏主界面 headless 测试（docs/10 §12.4：三栏导航/列表选择/过滤/改判入口）。
/// 真实 Bootstrapper（DEV 假通道 + 临时数据目录）拉起 Shell；断言经共享 ViewModel——两 UI 行为一致的锚点。</summary>
public class ShellHeadlessTests
{
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public async Task Onboarding_Connect_ShowsInbox_WithSeedMails()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Show();
            Pump();
            var vm = (MainViewModel)shell.DataContext!;
            vm.IsOnboarding.Should().BeTrue("无账户时先入 Onboarding（D-44 单窗双态）");

            vm.PrivacyAccepted = true; // 05 §3.1 隐私勾选前置
            await vm.ConnectCommand.ExecuteAsync(null);
            Pump();

            vm.IsOnboarding.Should().BeFalse();
            vm.Mails.Should().HaveCount(21, "DEV 种子 21 封（S18 增补 1 封 Canvas 摘要，含 2 条 DDL）");
        }
        finally
        {
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Selecting_Mail_MarksRead_And_RendersReaderBlocks()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            Pump();

            var first = vm.Mails[0];
            first.IsRead.Should().BeFalse();
            vm.SelectedMail = first;
            Pump();

            first.IsRead.Should().BeTrue("点击即标已读（UC-04）");
            shell.ReaderBlocks.Should().NotBeEmpty("阅读窗格应渲染净化块（正文或预览回落）");
        }
        finally
        {
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task UnreadOnlyToggle_HidesReadMails()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            Pump();

            vm.SelectedMail = vm.Mails[0]; // 标一封已读
            Pump();
            var unreadBefore = vm.Mails.Count(m => !m.IsRead);

            vm.UnreadOnly = true;
            await vm.ToggleUnreadOnlyCommand.ExecuteAsync(null);
            Pump();

            vm.Mails.Should().OnlyContain(m => !m.IsRead, "仅未读过滤（05 §3.2）");
            vm.Mails.Count.Should().Be(unreadBefore);
        }
        finally
        {
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ApplyCorrection_UpdatesCategory_AndMarksManualSource()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            Pump();

            vm.SelectedMail = vm.Mails[0];
            var targetId = vm.Mails[0].Id;
            await vm.ApplyCorrectionAsync("career", CancellationToken.None);
            Pump();

            // LoadInboxAsync 会重建行集合——按 Id 重查而非持旧引用
            vm.Mails.First(m => m.Id == targetId).Category.Should().Be("career", "改判立即生效（UC-06）");
        }
        finally
        {
            host.Dispose();
        }
    }

    internal static string FindRepoArtifacts()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MailHelper.sln")))
        {
            dir = dir.Parent!;
        }

        var path = Path.Combine(dir!.FullName, "artifacts", "screens", "avalonia");
        Directory.CreateDirectory(path);
        return path;
    }
}

/// <summary>headless 渲染帧截图（零窗口/零焦点抢占；替代抢前台截屏——后者在人机共用时不可靠且有侵扰性）。</summary>
public class ShellScreenshotTests
{
    [AvaloniaFact]
    public async Task Capture_Onboarding_And_Inbox_Frames()
    {
        var artifacts = ShellHeadlessTests.FindRepoArtifacts();

        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.CaptureRenderedFrame()?.Save(Path.Combine(artifacts, "ms5-onboarding.png"));

            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            vm.SelectedMail = vm.Mails.FirstOrDefault();
            Dispatcher.UIThread.RunJobs();
            shell.CaptureRenderedFrame()?.Save(Path.Combine(artifacts, "ms5-inbox.png"));

            // MS7 帧：规则页 / 设置页（装入 Window 宿主后截帧）
            CapturePage(host, new RulesPage { DataContext = ViewModelRules(host) }, "ms7-rules.png", artifacts);
            CapturePage(host, new SettingsPage(
                host.Services.GetRequiredService<MailHelper.Core.Abstractions.ICategoryStore>(),
                host.Services.GetRequiredService<MailHelper.Core.Abstractions.IAutoStarter>())
            { DataContext = ViewModelSettings(host) }, "ms7-settings.png", artifacts);

            File.Exists(Path.Combine(artifacts, "ms5-inbox.png")).Should().BeTrue();
            File.Exists(Path.Combine(artifacts, "ms7-rules.png")).Should().BeTrue();
            File.Exists(Path.Combine(artifacts, "ms7-settings.png")).Should().BeTrue();
        }
        finally
        {
            host.Dispose();
        }
    }

    private static void CapturePage(IHost host, UserControl page, string fileName, string artifacts)
    {
        var window = new Window { Width = 1100, Height = 700, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(artifacts, fileName));
        window.Close();
    }

    private static RulesViewModel ViewModelRules(IHost host) =>
        host.Services.GetRequiredService<RulesViewModel>();

    private static SettingsViewModel ViewModelSettings(IHost host) =>
        host.Services.GetRequiredService<SettingsViewModel>();
}
