using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MailHelper.Core;

namespace MailHelper.App;

/// <summary>重要度色卡（05 章 §5.1：P0 #D13438 / P1 #F7630C / P2 #0078D4 / P3 #8A8886）。</summary>
public sealed class ImportanceToBrushConverter : IValueConverter
{
    private static readonly Brush P0 = Freeze("#D13438");
    private static readonly Brush P1 = Freeze("#F7630C");
    private static readonly Brush P2 = Freeze("#0078D4");
    private static readonly Brush P3 = Freeze("#8A8886");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Importance importance
            ? importance switch
            {
                Importance.P0 => P0,
                Importance.P1 => P1,
                Importance.P2 => P2,
                _ => P3,
            }
            : P3;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

public sealed class CountToBadgeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count and > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>计数为 0 → 可见（空态面板）；>0 → 折叠。</summary>
public sealed class CountToEmptyVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>计数 → 无障碍名称「未读数N」（UIA 探测用，徽章数字变化实时反映）。</summary>
public sealed class CountToUnreadNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count and > 0 ? $"未读数{count}" : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>非 null → 可见（阅读窗格头）。</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
