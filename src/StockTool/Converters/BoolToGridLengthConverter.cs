using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StockTool.Converters;

// true/false → 指定宽度；参数可为数字、*，或 trueWidth|falseWidth
public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool show = value is true;
        string? param = parameter?.ToString();

        if (!string.IsNullOrEmpty(param) && param.Contains('|'))
        {
            var parts = param.Split('|', 2);
            return ParseLength(show ? parts[0] : parts[1], culture);
        }

        if (!show) return new GridLength(0);
        return ParseLength(string.IsNullOrEmpty(param) ? "80" : param, culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static GridLength ParseLength(string text, CultureInfo culture)
    {
        text = text.Trim();
        if (string.Equals(text, "*", StringComparison.Ordinal))
            return new GridLength(1, GridUnitType.Star);
        if (double.TryParse(text, NumberStyles.Any, culture, out var width))
            return new GridLength(width);
        return new GridLength(80);
    }
}
