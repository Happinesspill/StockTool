using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StockTool.Converters;

/// <summary>
/// Converts opacity value (0.0–1.0) to a SolidColorBrush.
/// 0.0 = transparent white, 1.0 = solid white.
/// </summary>
public class OpacityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double opacity = value switch
        {
            double d => d,
            int i => i,
            _ => 1.0
        };

        opacity = Math.Max(0, Math.Min(1, opacity));
        byte alpha = (byte)(opacity * 255);

        // default base color is white; parameter can be a hex string like "CCCCCC"
        Color baseColor = Colors.White;
        if (parameter is string hex && hex.Length == 6)
        {
            try
            {
                byte r = byte.Parse(hex[..2], NumberStyles.HexNumber);
                byte g = byte.Parse(hex[2..4], NumberStyles.HexNumber);
                byte b = byte.Parse(hex[4..6], NumberStyles.HexNumber);
                baseColor = Color.FromRgb(r, g, b);
            }
            catch { }
        }

        byte finalAlpha = (byte)(alpha * baseColor.A / 255);
        return new SolidColorBrush(Color.FromArgb(finalAlpha, baseColor.R, baseColor.G, baseColor.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
