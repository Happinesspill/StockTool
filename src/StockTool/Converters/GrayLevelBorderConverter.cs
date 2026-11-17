using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StockTool.Converters;

/// <summary>当前色阶与 ConverterParameter 相等时返回蓝色描边，否则浅灰。</summary>
public sealed class GrayLevelBorderConverter : IValueConverter
{
    private static readonly Brush Selected = Create("#3B7CFF");
    private static readonly Brush Normal = Create("#CCCCCC");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int current = value is int i ? i : 0;
        int target = parameter is int p
            ? p
            : int.TryParse(parameter?.ToString(), out int parsed) ? parsed : -1;
        return current == target ? Selected : Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Create(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        brush.Freeze();
        return brush;
    }
}
