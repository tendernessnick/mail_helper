using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using MailHelper.App.Avalonia;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using Xunit;

namespace MailHelper.App.Avalonia.Tests;

public sealed class ThemeContrastTests
{
    [AvaloniaFact]
    public void ButtonTemplates_StayReadableThroughPointerPressAndDisable_InBothThemes()
    {
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var normal = new Button { Content = "普通操作" };
            var primary = new Button { Content = "保存规则", Classes = { "primaryAction" } };
            var secondary = new Button { Content = "取消", Classes = { "navItem" } };
            var danger = new Button { Content = "删除类别", Classes = { "dangerAction" } };
            var panel = new StackPanel { Margin = new Thickness(30), Spacing = 14 };
            foreach (var button in new[] { normal, primary, secondary, danger }) panel.Children.Add(button);
            var window = new Window { Content = panel, Width = 500, Height = 400, RequestedThemeVariant = theme };
            window.Bind(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("BrushSurface"));
            window.Show();
            try
            {
                Layout(window);
                foreach (var button in new[] { normal, primary, secondary, danger })
                {
                    window.MouseMove(new Point(490, 390));
                    Layout(window);
                    AssertButton(button, 4.5, $"{theme} {button.Content} normal");
                    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point);
                    Layout(window);
                    button.IsPointerOver.Should().BeTrue();
                    AssertButton(button, 4.5, $"{theme} {button.Content} hover");
                    window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                    Layout(window);
                    button.IsPressed.Should().BeTrue();
                    AssertButton(button, 4.5, $"{theme} {button.Content} pressed");
                    window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                    button.IsEnabled = false;
                    Layout(window);
                    AssertButton(button, 3, $"{theme} {button.Content} disabled");
                    button.IsEnabled = true;
                }
                window.CaptureRenderedFrame()?.Save(Path.Combine(ShellHeadlessTests.FindRepoArtifacts(), $"polish-buttons-{theme}.png"));
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void RealRuleDialog_InputsLabelsAndPopupItems_UseReadableThemeColors()
    {
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var draft = new ClassifyRule(Guid.NewGuid(), "主题测试", RuleKind.SubjectKeyword, "assignment",
                CategoryIds.Course, Importance.P1, 3, 100, true, RuleSource.User);
            var dialog = new RuleEditWindow(draft) { RequestedThemeVariant = theme };
            dialog.Show();
            try
            {
                Layout(dialog);
                foreach (var text in dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text)))
                    AssertText(text, 4.5, $"{theme} 规则对话框 {text.Text}");
                foreach (var name in new[] { "KindBox", "CategoryBox", "ImportanceBox" })
                {
                    var combo = dialog.FindControl<ComboBox>(name)!;
                    combo.BringIntoView();
                    Layout(dialog);
                    var text = combo.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrWhiteSpace(t.Text));
                    AssertText(text, 4.5, $"{theme} {name} closed");
                    combo.IsDropDownOpen = true;
                    Layout(dialog);
                    var popupItems = combo.Items.Cast<object>().Select(i => combo.ContainerFromItem(i)).OfType<ComboBoxItem>().ToArray();
                    popupItems.Should().NotBeEmpty();
                    foreach (var item in popupItems)
                    {
                        var popupText = item.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrWhiteSpace(t.Text));
                        AssertText(popupText, 4.5, $"{theme} {name} open item");
                    }
                    combo.IsDropDownOpen = false;
                    Layout(dialog);
                }
                dialog.CaptureRenderedFrame()?.Save(Path.Combine(ShellHeadlessTests.FindRepoArtifacts(), $"polish-rule-dialog-{theme}.png"));
            }
            finally { dialog.Close(); }
        }
    }

    [AvaloniaFact]
    public void DynamicPriorityLabelsAndOverdueText_StayReadable_WhenThemeChanges()
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        foreach (var label in new[] { "p0", "p1", "p2", "p3" })
            panel.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Classes = { "importanceText", label } });
        panel.Children.Add(new TextBlock { Text = "已逾期", Classes = { "relativeTime", "overdue" } });
        var window = new Window { Content = panel, Width = 460, Height = 320 };
        window.Bind(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("BrushSurface"));
        window.Show();
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Light })
            {
                window.RequestedThemeVariant = theme;
                Layout(window);
                foreach (var text in panel.Children.OfType<TextBlock>()) AssertText(text, 4.5, $"{theme} {text.Text}");
            }
        }
        finally { window.Close(); }
    }

    private static void AssertButton(Button button, double minimum, string label)
    {
        var text = button.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrWhiteSpace(t.Text));
        AssertText(text, minimum, label);
    }

    private static void AssertText(TextBlock text, double minimum, string label)
    {
        var background = Colors.White;
        var layers = new List<Color>();
        var opacity = 1d;
        for (Visual? current = text; current is not null; current = current.GetVisualParent())
        {
            opacity *= current.Opacity;
            IBrush? brush = current switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                ContentPresenter presenter => presenter.Background,
                TopLevel top => top.Background,
                _ => null,
            };
            if (brush is ISolidColorBrush color) layers.Add(color.Color);
        }
        for (var i = layers.Count - 1; i >= 0; i--) background = Composite(layers[i], background, 1);
        text.Foreground.Should().BeAssignableTo<ISolidColorBrush>();
        var foreground = (ISolidColorBrush)text.Foreground!;
        var effective = Composite(foreground.Color, background, opacity * foreground.Opacity);
        var ratio = (Math.Max(Luminance(effective), Luminance(background)) + .05) /
            (Math.Min(Luminance(effective), Luminance(background)) + .05);
        ratio.Should().BeGreaterThanOrEqualTo(minimum - .02, $"{label} actual text {effective} on {background} contrast {ratio:F2}");
    }

    private static Color Composite(Color foreground, Color background, double opacity)
    {
        var alpha = foreground.A / 255d * opacity;
        byte Mix(byte f, byte b) => (byte)Math.Round(f * alpha + b * (1 - alpha));
        return Color.FromRgb(Mix(foreground.R, background.R), Mix(foreground.G, background.G), Mix(foreground.B, background.B));
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte b) { var x = b / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
