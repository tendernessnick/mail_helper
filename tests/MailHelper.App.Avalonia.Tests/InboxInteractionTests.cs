using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAssertions;
using MailHelper.App.Avalonia;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Reading;
using MailHelper.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailHelper.App.Avalonia.Tests;

public class InboxInteractionTests
{
    [AvaloniaFact]
    public async Task SharedNavigationCommands_MountPages_AndSearchReturnsToInbox()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var inbox = shell.FindControl<ContentControl>("PageHost")!.Content;

            vm.ShowSettingsCommand.Execute(null); // 托盘入口执行的正是此命令。
            shell.FindControl<ContentControl>("PageHost")!.Content.Should().BeOfType<SettingsPage>();
            shell.FindControl<Button>("NavSettings")!.Classes.Should().Contain("checked");
            vm.ShowRulesCommand.Execute(null);
            shell.FindControl<ContentControl>("PageHost")!.Content.Should().BeOfType<RulesPage>();

            vm.SearchText = "from:finance";
            await vm.SearchCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            shell.FindControl<ContentControl>("PageHost")!.Content.Should().BeSameAs(inbox);
            shell.FindControl<Button>("NavInbox")!.Classes.Should().Contain("checked");
            vm.Mails.Should().NotBeEmpty();
        }
        finally
        {
            shell.Close();
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task KeyboardSearch_FocusesSubmitsAndClears_WithUsefulEmptyState()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            vm.ShowRulesCommand.Execute(null);

            var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            shell.KeyPressQwerty(PhysicalKey.F, modifiers);
            shell.KeyReleaseQwerty(PhysicalKey.F, modifiers);
            Dispatcher.UIThread.RunJobs();
            var search = shell.FindControl<TextBox>("SearchBox")!;
            search.IsFocused.Should().BeTrue();
            vm.IsInboxView.Should().BeTrue();

            shell.KeyTextInput("mailhelper-no-match-987654");
            shell.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            shell.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await EventuallyAsync(() => vm.IsSearchMode && !vm.IsLoading);
            vm.Mails.Should().BeEmpty();
            shell.FindControl<TextBlock>("ListEmptyState")!.Text.Should().Be(vm.EmptyStateText);
            vm.EmptyStateText.Should().NotBe("该分类暂无邮件");
            shell.FindControl<Button>("ClearSearchButton")!.IsVisible.Should().BeTrue();

            shell.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            shell.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await EventuallyAsync(() => !vm.IsSearchMode && !vm.IsLoading && vm.Mails.Count == 21);
            search.Text.Should().BeEmpty();
        }
        finally
        {
            shell.Close();
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task MinimumWindowWidth_KeepsSearchActionsWithinWindow()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Width = shell.MinWidth;
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            vm.SearchText = "from:finance";
            await vm.SearchCommand.ExecuteAsync(null); // 清除按钮出现时也应容纳。
            vm.SelectedMail = vm.Mails[0];
            await EventuallyAsync(() => !shell.FindControl<StackPanel>("ReaderEmptyState")!.IsVisible);
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLayout();

            foreach (var name in new[] { "SearchBox", "SearchButton", "ClearSearchButton", "NavSettings", "CorrectButton" })
            {
                var control = shell.FindControl<Control>(name)!;
                var origin = control.TranslatePoint(default, shell)!.Value;
                origin.X.Should().BeGreaterThanOrEqualTo(0, name);
                (origin.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(shell.Bounds.Width + 1, name);
                control.Bounds.Width.Should().BeGreaterThan(20, name);
            }

            shell.CaptureRenderedFrame()?.Save(Path.Combine(ShellHeadlessTests.FindRepoArtifacts(), "inbox-min-width.png"));
        }
        finally
        {
            shell.Close();
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task LoadMoreButton_ReachesOlderSearchResults_WithoutLosingSelection()
    {
        var (host, shell) = TestBootstrap.BuildShell();
        try
        {
            shell.Show();
            var vm = (MainViewModel)shell.DataContext!;
            vm.PrivacyAccepted = true;
            await vm.ConnectCommand.ExecuteAsync(null);
            var now = DateTime.UtcNow;
            var messages = Enumerable.Range(0, 421).Select(i => new MailMessage(
                $"pagination-{i:D4}", "acc-1", null, $"Synthetic message {i}", "Pagination example",
                "pagination@example.com", "Synthetic test data", null, now.AddMinutes(-i), false,
                true, "other", Importance.P2, 1, "user", now, null, false)).ToArray();
            await host.Services.GetRequiredService<IMessageStore>().UpsertRangeAsync("acc-1", messages, CancellationToken.None);
            vm.SearchText = "from:pagination@example.com";
            await vm.SearchCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            vm.Mails.Should().HaveCount(200);
            vm.SelectedMail = vm.Mails[0];
            var selectedId = vm.SelectedMail.Id;
            var button = shell.FindControl<Button>("LoadMoreButton")!;

            Click(shell, button);
            await EventuallyAsync(() => !vm.IsLoading && vm.Mails.Count == 400);
            vm.SelectedMail!.Id.Should().Be(selectedId);
            Click(shell, button);
            await EventuallyAsync(() => !vm.IsLoading && vm.Mails.Count == 421);
            vm.Mails.Select(m => m.Id).Should().OnlyHaveUniqueItems();
            vm.HasMore.Should().BeFalse();
            button.IsVisible.Should().BeFalse();
        }
        finally
        {
            shell.Close();
            host.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task SlowPreviousBody_CannotOverwriteCurrentSelection_AndClearingHidesBody()
    {
        var (host, original) = TestBootstrap.BuildShell();
        var cache = new DelayedBodyCache();
        var vm = host.Services.GetRequiredService<MainViewModel>();
        var shell = new ShellWindow(vm,
            host.Services.GetRequiredService<RulesViewModel>(),
            host.Services.GetRequiredService<SettingsViewModel>(),
            host.Services.GetRequiredService<ScheduleViewModel>(), cache,
            host.Services.GetRequiredService<ICategoryStore>(),
            host.Services.GetRequiredService<IAutoStarter>()) { DataContext = vm };
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            vm.SelectedMail = Mail("old", "slow");
            vm.SelectedMail = Mail("current", "fast");
            await EventuallyAsync(() => shell.ReaderBlocks.OfType<SanitizedTextBlock>().Any(b => b.Text.Contains("current body")));
            cache.Slow.TrySetResult("<p>stale body</p>");
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
            shell.ReaderBlocks.OfType<SanitizedTextBlock>().Should().ContainSingle(b => b.Text.Contains("current body"));
            shell.ReaderBlocks.OfType<SanitizedTextBlock>().Should().NotContain(b => b.Text.Contains("stale body"));

            vm.SelectedMail = null;
            await EventuallyAsync(() => shell.ReaderBlocks.Count == 0);
            shell.FindControl<StackPanel>("ReaderEmptyState")!.IsVisible.Should().BeTrue();
        }
        finally
        {
            shell.Close();
            original.Close();
            host.Dispose();
        }
    }

    private static MailItemViewModel Mail(string id, string path) => new()
    {
        Id = id, Subject = id, FromDisplay = "Anonymous", Preview = "preview", ReceivedText = "now",
        Category = "other", Importance = Importance.P2, Confidence = 1, HtmlPath = path,
        HasAttachments = false, IsRead = true,
    };

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs();
        condition().Should().BeTrue("界面异步操作应在超时前完成");
    }

    private static void Click(Window shell, Control control)
    {
        shell.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), shell)!.Value;
        shell.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        shell.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class DelayedBodyCache : IBodyCache
    {
        public TaskCompletionSource<string?> Slow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> ReadAsync(string relativePath, CancellationToken ct) => relativePath == "slow"
            ? Slow.Task : Task.FromResult<string?>("<p>current body</p>");

        public Task<string> SaveAsync(string accountId, string html, CancellationToken ct) => throw new NotSupportedException();
    }
}
