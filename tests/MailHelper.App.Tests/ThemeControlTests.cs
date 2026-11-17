using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using MailHelper.App;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.ViewModels;
using MailHelper.Infrastructure.SystemIntegration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailHelper.App.Tests;

/// <summary>加载真正的 BAML 资源与控件模板，在独立 STA 上验证最终颜色。
/// 仅创建基础 Application，不运行产品 App/Bootstrapper，不访问 Outlook 或用户配置。</summary>
public sealed class ThemeControlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ButtonsAndInputs_ResolveReadableNormalAndDisabledTemplates(bool dark) =>
        await Sta.RunAsync(() =>
        {
            Palette(dark);
            var buttons = new[]
            {
                new Button { Content = "普通按钮" },
                new Button { Content = "主要操作", Style = Resource<Style>("PrimaryButton") },
                new Button { Content = "次要操作", Style = Resource<Style>("GhostButton") },
            };
            var text = new TextBox { Text = "同步间隔 5 分钟", Margin = new Thickness(0, 12, 0, 0) };
            var check = new CheckBox { Content = "临近截止时提醒", IsChecked = true };
            var panel = new StackPanel { Margin = new Thickness(24) };
            foreach (var button in buttons) panel.Children.Add(button);
            panel.Children.Add(text);
            panel.Children.Add(check);
            var window = Host(panel);
            try
            {
                foreach (var button in buttons)
                {
                    button.ApplyTemplate();
                    button.UpdateLayout();
                    Contrast(button.Foreground, Surface(button), Opacity(button), 4.5,
                        $"{(dark ? "深色" : "浅色")} {button.Content}正常态");
                    button.IsEnabled = false;
                    window.UpdateLayout();
                    var presenter = Visuals(button).OfType<ContentPresenter>().FirstOrDefault();
                    var effectiveOpacity = presenter is null ? Opacity(button) : Opacity(presenter);
                    Contrast(button.Foreground, Surface(presenter ?? (DependencyObject)button), effectiveOpacity, 3,
                        $"{button.Content}禁用态仍需可辨认");
                    button.IsEnabled = true;
                }
                Contrast(text.Foreground, Surface(text), Opacity(text), 4.5, "实际输入文字");
                Contrast(check.Foreground, Surface(check), Opacity(check), 4.5, "复选框标签");
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealRuleDialog_ScopedComboStyle_AndOpenedItemsRemainReadable(bool dark) =>
        await Sta.RunAsync(() =>
        {
            Palette(dark);
            var draft = new ClassifyRule(Guid.NewGuid(), "主题检查", RuleKind.SubjectKeyword,
                "assignment", CategoryIds.Course, Importance.P1, 3, 100, true, RuleSource.User);
            var dialog = new RuleEditDialog(draft, new LanguageService(new MemorySettings()))
            {
                ShowActivated = false, ShowInTaskbar = false, Left = -2600, Top = -2600,
            };
            dialog.Show();
            dialog.UpdateLayout();
            try
            {
                foreach (var name in new[] { "KindBox", "CategoryBox", "ImportanceBox" })
                {
                    var combo = (ComboBox)dialog.FindName(name);
                    Contrast(combo.Foreground, Surface(combo), Opacity(combo), 4.5, $"规则弹窗 {name}选中值");
                    combo.IsDropDownOpen = true;
                    combo.UpdateLayout();
                    Pump();
                    var popup = Visuals(combo).OfType<Popup>().FirstOrDefault()
                        ?? combo.Template.FindName("PART_Popup", combo) as Popup;
                    popup.Should().NotBeNull("应验证真正打开的下拉层，而不只看ComboBox壳");
                    var items = combo.Items.Cast<object>().Select(i => combo.ItemContainerGenerator.ContainerFromItem(i))
                        .OfType<ComboBoxItem>().ToArray();
                    items.Should().NotBeEmpty();
                    foreach (var item in items)
                        Contrast(item.Foreground, Surface(item), Opacity(item), 4.5, $"{name}下拉项目");
                    combo.IsDropDownOpen = false;
                }
                foreach (var button in Visuals(dialog).OfType<Button>())
                    Contrast(button.Foreground, Surface(button), Opacity(button), 4.5, "规则弹窗按钮");
            }
            finally { dialog.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextMenuAndPriorityLabels_UseReadableEffectiveSurfaces(bool dark) =>
        await Sta.RunAsync(() =>
        {
            Palette(dark);
            var button = new Button { Content = "更改分类" };
            var window = Host(button);
            var menu = new ContextMenu { PlacementTarget = button };
            menu.Items.Add(new MenuItem { Header = "课程学习" });
            menu.Items.Add(new MenuItem { Header = "职业发展" });
            try
            {
                menu.IsOpen = true;
                Pump();
                foreach (var item in menu.Items.Cast<MenuItem>())
                    Contrast(item.Foreground, Surface(item), Opacity(item), 4.5, "真正弹出的分类菜单");
                foreach (var level in new[] { "P0", "P1", "P2", "P3" })
                {
                    var foreground = Resource<Brush>($"Brush{level}Text");
                    foreach (var surface in new[] { "BrushSurface", "BrushRowSelected" })
                        Contrast(foreground, ColorOf(Resource<Brush>(surface)), 1, 4.5, $"{level} 在 {surface} 上");
                }
                foreach (var key in new[] { "BrushTextPrimary", "BrushTextSecondary", "BrushTextTertiary" })
                    foreach (var surface in new[] { "BrushSurface", "BrushSurfaceAlt", "BrushRowSelected", "BrushSidebar" })
                        Contrast(Resource<Brush>(key), ColorOf(Resource<Brush>(surface)), 1, 4.5, $"{key} 在 {surface} 上");
            }
            finally { menu.IsOpen = false; window.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThemedConfirmation_CanBeCancelled_AndOnlyConfirmsOnExplicitAction(bool dark) =>
        await Sta.RunAsync(() =>
        {
            Palette(dark);
            var owner = Host(new TextBlock { Text = "仅为主题验证，不连接账户" });
            Application.Current.MainWindow = owner;
            try
            {
                foreach (var action in new[] { "取消", "确认" })
                {
                    Exception? failure = null;
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "确认测试");
                        try
                        {
                            dialog.UpdateLayout();
                            foreach (var button in Visuals(dialog).OfType<Button>())
                                Contrast(button.Foreground, Surface(button), Opacity(button), 4.5, "实际确认弹窗按钮");
                            var selected = Visuals(dialog).OfType<Button>().Single(b => Equals(b.Content, action));
                            selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch (Exception ex) { failure = ex; dialog.Close(); }
                    }));
                    var result = WorkspaceDialog.Show("仅测试提示与确认按钮，不执行任何删除。", "确认测试", MessageBoxButton.YesNo);
                    failure.Should().BeNull();
                    result.Should().Be(action == "确认" ? MessageBoxResult.Yes : MessageBoxResult.No);
                }
            }
            finally { owner.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThemedScrollTrack_PreservesNativePageNavigation(bool dark) =>
        await Sta.RunAsync(() =>
        {
            Palette(dark);
            var content = new StackPanel();
            for (var i = 0; i < 50; i++) content.Children.Add(new TextBlock { Text = $"测试邮件 {i}", Height = 28 });
            var scroll = new ScrollViewer { Content = content, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
            var window = Host(scroll);
            try
            {
                var bar = Visuals(scroll).OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical);
                bar.ApplyTemplate();
                var next = (RepeatButton)bar.Template.FindName("After", bar);
                var command = (System.Windows.Input.RoutedCommand)next.Command;
                command.CanExecute(null, next).Should().BeTrue();
                command.Execute(null, next);
                window.UpdateLayout();
                scroll.VerticalOffset.Should().BeGreaterThan(0, "新样式应保留点击轨道向下翻页");
                var before = scroll.VerticalOffset;
                var previous = (RepeatButton)bar.Template.FindName("Before", bar);
                ((System.Windows.Input.RoutedCommand)previous.Command).Execute(null, previous);
                window.UpdateLayout();
                scroll.VerticalOffset.Should().BeLessThan(before, "点击前段轨道应向上翻页");
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualRuleCards_InheritThemeText_AndKeepActionsInsideTheViewport(bool dark) =>
        await Sta.RunAsync(() =>
        {
            Palette(dark);
            var previousDev = Environment.GetEnvironmentVariable("MAILHELPER_DEV");
            var previousPath = Environment.GetEnvironmentVariable("MAILHELPER_DATA_DIR");
            var testPath = Path.Combine(Path.GetTempPath(), "mh-theme-rules-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("MAILHELPER_DEV", "1");
            Environment.SetEnvironmentVariable("MAILHELPER_DATA_DIR", testPath);
            try
            {
                using var host = Bootstrapper.BuildHost(new WindowsSingleInstanceLock());
                var page = host.Services.GetRequiredService<RulesPage>();
                var window = Host(page);
                window.Width = 772;
                window.Height = 640;
                try
                {
                    Pump();
                    var rule = new ClassifyRule(Guid.NewGuid(), "课程项目与教学事务通知的自动分类规则", RuleKind.SubjectKeyword,
                        "assignment|course|project", CategoryIds.Course, Importance.P1, 3, 100, true, RuleSource.User);
                    var row = new RuleRowViewModel { Rule = rule, Enabled = true, SetEnabledAsync = (_, _) => Task.CompletedTask, RequestDelete = _ => { } };
                    ((ListBox)page.FindName("RulesList")).ItemsSource = new[] { row };
                    window.UpdateLayout();
                    var name = Visuals(page).OfType<TextBlock>().Single(t => t.Text == rule.Name);
                    Contrast(name.Foreground, Surface(name), Opacity(name), 4.5, "真实规则列表标题不能继承系统黑色");
                    foreach (var button in Visuals(page).OfType<Button>().Where(b => b.IsVisible))
                    {
                        Contrast(button.Foreground, Surface(button), Opacity(button), 4.5, "规则页操作");
                        var origin = button.TransformToAncestor(window).Transform(new Point());
                        origin.X.Should().BeGreaterThanOrEqualTo(0);
                        (origin.X + button.ActualWidth).Should().BeLessThanOrEqualTo(window.ActualWidth + 1,
                            $"{button.Content}需在内容区域内可操作");
                    }
                }
                finally { window.Close(); }
            }
            finally
            {
                Environment.SetEnvironmentVariable("MAILHELPER_DEV", previousDev);
                Environment.SetEnvironmentVariable("MAILHELPER_DATA_DIR", previousPath);
            }
        });

    private static void Palette(bool dark)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/MailHelper.App;component/{(dark ? "DarkDesignTokens" : "DesignTokens")}.xaml"),
        });
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MailHelper.App;component/Controls.xaml"),
        });
    }

    private static T Resource<T>(string key) => (T)Application.Current.FindResource(key);

    private static Window Host(UIElement content)
    {
        var window = new Window
        {
            Content = content, Width = 620, Height = 520, ShowInTaskbar = false, ShowActivated = false,
            Left = -2600, Top = -2600, Background = Resource<Brush>("BrushSurface"),
            Foreground = Resource<Brush>("BrushTextPrimary"),
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    private static Color Surface(DependencyObject start)
    {
        // Background on a Control is only a template input. Measure the Border that actually paints it.
        if (start is Control control)
        {
            control.ApplyTemplate();
            if (control.Template?.FindName("Surface", control) is Border painted)
                start = painted;
        }
        var layers = new List<Color>();
        for (DependencyObject? current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            Brush? brush = current switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                Window window => window.Background,
                ContentPresenter presenter => presenter.GetValue(TextElement.BackgroundProperty) as Brush,
                _ => null,
            };
            if (brush is SolidColorBrush solid) layers.Add(solid.Color);
        }
        var result = ColorOf(Resource<Brush>("BrushSurface"));
        for (var i = layers.Count - 1; i >= 0; i--) result = Composite(layers[i], result, 1);
        return result;
    }

    private static double Opacity(DependencyObject start)
    {
        var opacity = 1d;
        for (DependencyObject? current = start; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement element) opacity *= element.Opacity;
        return opacity;
    }

    private static Color ColorOf(Brush brush) => brush.Should().BeOfType<SolidColorBrush>().Which.Color;

    private static void Contrast(Brush foreground, Color background, double opacity, double minimum, string label)
    {
        var text = Composite(ColorOf(foreground), background, opacity * foreground.Opacity);
        var ratio = (Math.Max(Luminance(text), Luminance(background)) + 0.05) /
                    (Math.Min(Luminance(text), Luminance(background)) + 0.05);
        ratio.Should().BeGreaterThanOrEqualTo(minimum - 0.02,
            $"{label}：最终前景 {text} / 背景 {background}，对比度 {ratio:0.00}:1");
    }

    private static Color Composite(Color foreground, Color background, double opacity)
    {
        var alpha = foreground.A / 255d * opacity;
        byte Mix(byte f, byte b) => (byte)Math.Round(f * alpha + b * (1 - alpha));
        return Color.FromRgb(Mix(foreground.R, background.R), Mix(foreground.G, background.G), Mix(foreground.B, background.B));
    }

    private static double Luminance(Color c)
    {
        static double Linear(byte b) { var x = b / 255d; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    private static void Pump()
    {
        // Let real popup animation complete before measuring its effective opacity.
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class MemorySettings : ISettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken ct) => Task.CompletedTask;
    }

    private static class Sta
    {
        private static readonly Task<Dispatcher> Ready = Start();
        private static Task<Dispatcher> Start()
        {
            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }) { IsBackground = true, Name = "MailHelper-theme-test" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return ready.Task;
        }
        internal static async Task RunAsync(Action action)
        {
            var dispatcher = await Ready;
            await dispatcher.InvokeAsync(action).Task;
        }
    }
}
