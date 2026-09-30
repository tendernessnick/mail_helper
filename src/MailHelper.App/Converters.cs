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

/// <summary>S13-C 类别语义色（DesignTokens 类别色板）：头像底色/左栏图标底色。</summary>
public sealed class CategoryToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MailCategory category
            ? category switch
            {
                MailCategory.Course => FrozenCourse,
                MailCategory.Career => FrozenCareer,
                MailCategory.Admin => FrozenAdmin,
                MailCategory.Finance => FrozenFinance,
                MailCategory.Announce => FrozenAnnounce,
                MailCategory.Subscription => FrozenSubscription,
                _ => FrozenOther,
            }
            : FrozenOther;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static readonly Brush FrozenCourse = Freeze("#3A7BD5");
    private static readonly Brush FrozenCareer = Freeze("#0E9F6E");
    private static readonly Brush FrozenAdmin = Freeze("#7C5CDB");
    private static readonly Brush FrozenFinance = Freeze("#D97706");
    private static readonly Brush FrozenAnnounce = Freeze("#0E7490");
    private static readonly Brush FrozenSubscription = Freeze("#8C93A0");
    private static readonly Brush FrozenOther = Freeze("#64748B");

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

/// <summary>bool 取反（规则管理页：内置行禁用编辑/隐藏删除，05 §3.3）。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
