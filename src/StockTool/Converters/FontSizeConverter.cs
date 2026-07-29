using System.Globalization;
using System.Windows.Data;

namespace StockTool.Converters;

public class FontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double baseSize = value is int i ? i : System.Convert.ToDouble(value);
        double offset = 0;
        if (parameter is string s && double.TryParse(s, out var p))
            offset = p;
        else if (parameter is double d)
            offset = d;
        return baseSize + offset;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
