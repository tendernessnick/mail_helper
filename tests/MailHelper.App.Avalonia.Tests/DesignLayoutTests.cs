using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using MailHelper.App.Avalonia;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailHelper.App.Avalonia.Tests;

/// <summary>设计改版的布局回归：真实量取可视区域，不以控件存在或某个颜色值替代可用性。
/// 使用匿名种子与临时 SQLite；不接触已安装 MailHelper 或实际 Outlook 邮箱。</summary>
public class DesignLayoutTests
{
    [AvaloniaFact]
    public async Task LongMail_AtMinimumAndStandardSizes_KeepsSearchListAndBodyUsable()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            var now = DateTime.UtcNow;
            var stored = new MailMessage("design-long-mail", "acc-1", null,
                string.Concat(Enumerable.Repeat("课程项目最新安排与提交要求说明", 10)),
                "CityU School of Business — Programme Administration and Student Support Office",
                "design-layout@example.com", "匿名布局测试正文。即使主题与发件人很长，阅读内容仍应拥有可操作的空间。",
                null, now, true, true, CategoryIds.Course, Importance.P1, 1, "user", now, null, false);
            await host.Services.GetRequiredService<IMessageStore>().UpsertRangeAsync("acc-1", [stored], default);
            vm.SearchText = "from:design-layout@example.com";
            await vm.SearchCommand.ExecuteAsync(null);
            var longMail = vm.Mails.Single(m => m.Id == stored.Id);
            vm.SelectedMail = longMail;
            await EventuallyAsync(() => shell.ReaderBlocks.Count > 0);

            foreach (var (width, height, theme) in new[]
                     {
                         (980d, 620d, ThemeVariant.Light), (1280d, 800d, ThemeVariant.Light),
                         (980d, 620d, ThemeVariant.Dark),
                     })
            {
                shell.Width = width;
                shell.Height = height;
                shell.RequestedThemeVariant = theme;
                Layout(shell);
                foreach (var name in new[] { "SearchBox", "SearchButton", "CorrectButton", "ReaderPanel" })
                {
                    AssertInside(shell.FindControl<Control>(name)!, shell, name);
                }

                var list = shell.FindControl<ListBox>("MailList")!;
                var body = shell.FindControl<ScrollViewer>("ReaderBodyScroll")!;
                list.Bounds.Width.Should().BeGreaterThanOrEqualTo(260, "列表需容纳发件人、主题和时间");
                list.Bounds.Height.Should().BeGreaterThanOrEqualTo(180, "最小高度仍能浏览多封邮件");
                body.Bounds.Width.Should().BeGreaterThanOrEqualTo(250, "正文不能被长邮件头挤成窄条");
                body.Bounds.Height.Should().BeGreaterThanOrEqualTo(120, "长主题不能把正文挤出可视区");
                list.SelectedItem.Should().BeSameAs(longMail, "改布局或主题不应失去当前阅读选择");
                shell.CaptureRenderedFrame()?.Save(Path.Combine(ShellHeadlessTests.FindRepoArtifacts(),
                    $"design-inbox-{width:0}-{height:0}-{(theme == ThemeVariant.Dark ? "dark" : "light")}.png"));
            }
        }
        finally
        {
            shell.Close();
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task LongCustomCategory_StaysInsideSidebar_AndCanBeSelected()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Width = 980;
            shell.Height = 620;
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            var category = new CategoryItemViewModel
            {
                CategoryId = "design-custom", Label = "课程资料与学生事务需要人工核对的长名称自定义类别",
                Icon = "◇", ColorHex = "#17786B", IsNeedsReviewEntry = false, Count = 9999,
            };
            vm.Categories.Add(category);
            var navigation = shell.FindControl<ListBox>("CategoryNav")!;
            navigation.ScrollIntoView(category);
            Layout(shell);
            var item = navigation.ContainerFromItem(category)!;
            item.Should().NotBeNull("新增类别应能滚动进入导航可视区");
            AssertInside(item, shell, "自定义分类导航项");
            var label = item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == category.Label);
            label.Bounds.Width.Should().BeLessThan(navigation.Bounds.Width, "长名称应受约束而非横向撑开导航");
            navigation.SelectedItem = category;
            await EventuallyAsync(() => !vm.IsLoading);
            vm.SelectedCategory.Should().BeSameAs(category);
            shell.FindControl<Border>("ReaderPanel")!.Bounds.Width.Should().BeGreaterThan(300);
        }
        finally
        {
            shell.Close();
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task SecondaryPages_AtMinimumSize_ExposeActionsWithoutHorizontalClipping()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Width = 980;
            shell.Height = 620;
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            var pageHost = shell.FindControl<ContentControl>("PageHost")!;

            vm.ShowScheduleCommand.Execute(null);
            await host.Services.GetRequiredService<ScheduleViewModel>().RefreshAsync(default);
            Layout(shell);
            var schedule = pageHost.Content.Should().BeOfType<SchedulePage>().Which;
            var refresh = Buttons(schedule).First(b => b.Content?.ToString()?.Contains("刷新") == true);
            var refreshCommand = typeof(ScheduleViewModel).GetProperty("RefreshCommand")?
                .GetValue(host.Services.GetRequiredService<ScheduleViewModel>());
            refreshCommand.Should().NotBeNull("日程刷新必须有真正的生成命令，而不是失效的绑定名称");
            refresh.Command.Should().BeSameAs(refreshCommand);
            AssertInside(refresh, shell, "日程刷新");
            var complete = Buttons(schedule).First(b => b.Content?.ToString() == "完成");
            complete.BringIntoView();
            Layout(shell);
            AssertInside(complete, shell, "日程完成操作");

            vm.ShowRulesCommand.Execute(null);
            await host.Services.GetRequiredService<RulesViewModel>().LoadAsync();
            Layout(shell);
            var rules = pageHost.Content.Should().BeOfType<RulesPage>().Which;
            var newRule = Buttons(rules).First(b => b.Content?.ToString()?.Contains("新建") == true);
            AssertInside(newRule, shell, "新建规则");
            var ruleList = rules.GetVisualDescendants().OfType<ListBox>().First();
            AssertInside(ruleList, shell, "规则列表");
            ruleList.Bounds.Height.Should().BeGreaterThan(150, "规则列表需要自己的滚动空间");

            vm.ShowSettingsCommand.Execute(null);
            await host.Services.GetRequiredService<SettingsViewModel>().LoadAsync(default);
            Layout(shell);
            var settings = pageHost.Content.Should().BeOfType<SettingsPage>().Which;
            var input = settings.FindControl<TextBox>("NewCategoryName")!;
            input.BringIntoView();
            Layout(shell);
            AssertInside(input, shell, "自定义类别输入框");
            var settingsVm = host.Services.GetRequiredService<SettingsViewModel>();
            settingsVm.UpdateReady = true;
            settingsVm.UpdateStatus = "匿名测试：更新包已校验，当前状态说明较长也应自动换行，不将安装按钮推出窗口。";
            Layout(shell);
            var install = Buttons(settings).Single(b => ReferenceEquals(b.Command, settingsVm.ApplyUpdateCommand));
            install.BringIntoView();
            Layout(shell);
            AssertInside(install, shell, "更新安装操作");
            var scroll = settings.GetVisualDescendants().OfType<ScrollViewer>()
                .First(s => s.Extent.Height > s.Viewport.Height + 20);
            scroll.Offset.Y.Should().BeGreaterThan(0, "长设置页应通过页面滚动访问底部操作");
        }
        finally
        {
            shell.Close();
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task KeyboardFocus_TraversesSearchAction_WithoutLeavingVisibleViewport()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Width = 980;
            shell.Height = 620;
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            vm.SelectedMail = vm.Mails[0];
            var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            shell.KeyPressQwerty(PhysicalKey.F, modifiers);
            shell.KeyReleaseQwerty(PhysicalKey.F, modifiers);
            shell.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            shell.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Layout(shell);
            var searchAction = shell.FindControl<Button>("SearchButton")!;
            searchAction.IsFocused.Should().BeTrue("从搜索框按Tab应能直接到达搜索操作");
            AssertInside(searchAction, shell, "键盘焦点搜索按钮");
            shell.CaptureRenderedFrame()?.Save(Path.Combine(ShellHeadlessTests.FindRepoArtifacts(), "design-keyboard-focus.png"));
        }
        finally
        {
            shell.Close();
            host.Dispose();
        }
    }

    private static IEnumerable<Button> Buttons(Control root) => root.GetVisualDescendants().OfType<Button>();

    private static void Layout(Window shell)
    {
        Dispatcher.UIThread.RunJobs();
        shell.UpdateLayout();
    }

    private static void AssertInside(Control control, Window window, string reason)
    {
        control.Should().NotBeNull(reason);
        control.IsVisible.Should().BeTrue(reason);
        control.Bounds.Width.Should().BeGreaterThan(12, reason);
        control.Bounds.Height.Should().BeGreaterThan(12, reason);
        var origin = control.TranslatePoint(default, window)!.Value;
        origin.X.Should().BeGreaterThanOrEqualTo(-1, reason);
        origin.Y.Should().BeGreaterThanOrEqualTo(-1, reason);
        (origin.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(window.Bounds.Width + 1, reason);
        (origin.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(window.Bounds.Height + 1, reason);
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(6);
        while (!predicate() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs();
        predicate().Should().BeTrue("异步界面操作须在期限内完成");
    }
}
