using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MailHelper.App.Avalonia;

/// <summary>ColorHex（类别注册表）→ IBrush（MS3 左栏图标底色；转换失败回退中性灰）。</summary>
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

/// <summary>计数 > 0 → 可见（左栏未读徽章；0 隐藏）。</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public static readonly CountToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
