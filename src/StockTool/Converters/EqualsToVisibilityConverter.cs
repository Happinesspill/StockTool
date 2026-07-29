using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StockTool.Converters;

/// <summary>
/// value 与 ConverterParameter 相等时 Visible，否则 Collapsed。
/// ConverterParameter 前缀 "!" 表示取反。
/// </summary>
public class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string expected = parameter?.ToString() ?? "";
        bool invert = expected.StartsWith('!');
        if (invert) expected = expected[1..];

        bool equal = string.Equals(value?.ToString(), expected, StringComparison.Ordinal);
        if (invert) equal = !equal;
        return equal ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
