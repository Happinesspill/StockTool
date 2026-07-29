using System.Globalization;
using System.Windows.Data;

namespace StockTool.Converters;

/// <summary>value(double) + ConverterParameter(double) → double，用于 MaxHeight = 窗口高 + 偏移</summary>
public class DoubleOffsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double d) return 560.0;
        double offset = 0;
        if (parameter != null)
            double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out offset);
        return Math.Max(320, d + offset);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
