using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MailHelper.ViewModels;


namespace MailHelper.App.Avalonia;

/// <summary>ColorHex（类别注册表/重要度卡）→ IBrush（转换失败回退中性灰）。</summary>
public sealed class ColorHexToBrushConverter : IValueConverter
{
    public static readonly ColorHexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && Color.TryParse(hex, out var color))
        {
            return new SolidColorBrush(color);
        }

        return new SolidColorBrush(Color.Parse("#64748B"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>类别 ID → 类别色画刷（CategoryCatalog 动态取色，S14-C；未知回退中性灰）。</summary>
public sealed class CategoryColorConverter : IValueConverter
{
    public static readonly CategoryColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value is string id ? CategoryCatalog.ColorOf(id) : "#64748B";
        return Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : new SolidColorBrush(Color.Parse("#64748B"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>计数 > 0 → 可见（左栏未读徽章；0 隐藏）。</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public static readonly CountToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>计数 == 0 → 可见（列表空态；对照 CountToVisibility）。</summary>
public sealed class InverseCountToVisibilityConverter : IValueConverter
{
    public static readonly InverseCountToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not int count || count == 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool 取反（IsOnboarding ↔ 三栏显示切换等）。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>非空引用 → true（阅读窗格有选中邮件时显示正文面板）。</summary>
public sealed class NotNullToBoolConverter : IValueConverter
{
    public static readonly NotNullToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>逾期 bool → 强调红/中性文字画刷（S18 日程页 RelativeText；true=BrushDanger）。</summary>
public sealed class OverdueBrushConverter : IValueConverter
{
    public static readonly OverdueBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.Parse("#D13438"))
            : new SolidColorBrush(Color.Parse("#8A8886"));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
