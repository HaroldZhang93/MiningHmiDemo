// 杂项值转换器。HexToBrushConverter：把 "#RRGGBB" 字符串转成画刷，
// 让设备说明卡片能按"系统分组"用不同的强调色（数据里只存色值字符串）。
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HmiApp;

/// <summary>bool→Visibility，但取反：true→Collapsed，false→Visible。用于"收起时才显示"的提示。</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value as string;
        if (string.IsNullOrEmpty(hex)) return Brushes.Transparent;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
