using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace StockTool.Controls;

/// <summary>十字光标绘制辅助，分时/K 线控件共用</summary>
internal static class CrosshairRenderer
{
    private const double FontSize = 10;
    private static readonly Typeface LabelTypeface = new("Microsoft YaHei");

    /// <summary>十字虚线：竖线 top→bottom，横线 left→right</summary>
    public static void DrawCrossLines(DrawingContext dc, double x, double y, double left, double right,
        double top, double bottom)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(170, 90, 90, 90)), 0.7)
        {
            DashStyle = new DashStyle([3, 3], 0)
        };
        pen.Freeze();
        dc.DrawLine(pen, new Point(x, top), new Point(x, bottom));
        dc.DrawLine(pen, new Point(left, y), new Point(right, y));
    }

    /// <summary>光标点（白描边，避免与折线混在一起）</summary>
    public static void DrawPoint(DrawingContext dc, double x, double y, Color color)
    {
        var fill = new SolidColorBrush(color);
        fill.Freeze();
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 1.2);
        edge.Freeze();
        dc.DrawEllipse(fill, edge, new Point(x, y), 3, 3);
    }

    /// <summary>轴标签：右边缘贴 rightEdgeX，垂直居中于 centerY</summary>
    public static void DrawTag(DrawingContext dc, string text, double centerY, double rightEdgeX, Color background)
    {
        if (string.IsNullOrEmpty(text)) return;

        var ft = Format(text, Colors.White);
        double w = ft.Width + 8;
        double h = ft.Height + 3;
        var rect = new Rect(rightEdgeX - w, centerY - h / 2, w, h);

        var brush = new SolidColorBrush(background);
        brush.Freeze();
        dc.DrawRoundedRectangle(brush, null, rect, 3, 3);
        dc.DrawText(ft, new Point(rect.X + 4, rect.Y + 1.5));
    }

    /// <summary>底部时间标签：水平居中于 x，并限制在 [left, right] 内</summary>
    public static void DrawTimeTag(DrawingContext dc, string text, double x, double centerY, double left, double right)
    {
        if (string.IsNullOrEmpty(text)) return;

        var ft = Format(text, Colors.White);
        double w = ft.Width + 10;
        double h = ft.Height + 3;
        double half = w / 2;
        double cx = Math.Clamp(x, left + half, Math.Max(left + half, right - half));
        var rect = new Rect(cx - half, centerY - h / 2, w, h);

        var brush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A));
        brush.Freeze();
        dc.DrawRoundedRectangle(brush, null, rect, 3, 3);
        dc.DrawText(ft, new Point(rect.X + 5, rect.Y + 1.5));
    }

    private static FormattedText Format(string text, Color color) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, FontSize,
            new SolidColorBrush(color), 1.0);
}
