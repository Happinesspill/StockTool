using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StockTool.Core.Indicators;
using StockTool.Core.Models;

namespace StockTool.Controls;

public class SparklineControl : Control
{
    private const double BaseHeight = 180;
    private const double SubPaneHeight = 78;

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(List<decimal>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AvgPointsProperty =
        DependencyProperty.Register(nameof(AvgPoints), typeof(List<decimal>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaseLineProperty =
        DependencyProperty.Register(nameof(BaseLine), typeof(decimal), typeof(SparklineControl),
            new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsUpProperty =
        DependencyProperty.Register(nameof(IsUp), typeof(bool), typeof(SparklineControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(nameof(ShowLabels), typeof(bool), typeof(SparklineControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnShowLabelsChanged));

    public static readonly DependencyProperty VisibleSubCountProperty =
        DependencyProperty.Register(nameof(VisibleSubCount), typeof(int), typeof(SparklineControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnSubCountChanged));

    private static readonly Typeface LabelTypeface = new("Microsoft YaHei");
    private static readonly Color ColorRed = Color.FromRgb(217, 48, 37);
    private static readonly Color ColorGreen = Color.FromRgb(26, 140, 63);
    private static readonly Color ColorGold = Color.FromRgb(196, 140, 60);
    private static readonly Color ColorBlue = Color.FromRgb(64, 120, 200);

    public List<decimal>? Points
    {
        get => (List<decimal>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public List<decimal>? AvgPoints
    {
        get => (List<decimal>?)GetValue(AvgPointsProperty);
        set => SetValue(AvgPointsProperty, value);
    }

    public decimal BaseLine
    {
        get => (decimal)GetValue(BaseLineProperty);
        set => SetValue(BaseLineProperty, value);
    }

    public bool IsUp
    {
        get => (bool)GetValue(IsUpProperty);
        set => SetValue(IsUpProperty, value);
    }

    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>0=仅分时，1=+MACD，2=+KDJ，3=+RSI（详情大图）</summary>
    public int VisibleSubCount
    {
        get => (int)GetValue(VisibleSubCountProperty);
        set => SetValue(VisibleSubCountProperty, value);
    }

    static SparklineControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SparklineControl),
            new FrameworkPropertyMetadata(typeof(SparklineControl)));
    }

    private static void OnShowLabelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SparklineControl c) c.UpdateHeight();
    }

    private static void OnSubCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SparklineControl c)
        {
            c.UpdateHeight();
            c.InvalidateVisual();
        }
    }

    private void UpdateHeight()
    {
        if (!ShowLabels) return;
        Height = BaseHeight + Math.Clamp(VisibleSubCount, 0, 3) * SubPaneHeight;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var points = Points;
        if (points == null || points.Count < 2) return;

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        bool showLabels = ShowLabels;
        int subCount = showLabels ? Math.Clamp(VisibleSubCount, 0, 3) : 0;
        double subTotal = subCount * SubPaneHeight;
        double mainH = Math.Max(40, h - subTotal);

        DrawPricePane(dc, points, w, mainH, showLabels);

        if (subCount <= 0) return;

        // 用滚动高低价构造伪 K 线，供 KDJ 等指标计算
        var bars = ToPseudoKlines(points);
        var macd = IndicatorCalculator.Macd(bars);
        var kdj = IndicatorCalculator.Kdj(bars);
        var rsi = IndicatorCalculator.Rsi(bars);

        int n = points.Count;
        int startIdx = 0;
        double spacing = (w - 16) / (n + 0.5);

        double y = mainH;
        if (subCount >= 1)
        {
            DrawMacdPane(dc, startIdx, n, w, y, SubPaneHeight, spacing, macd);
            y += SubPaneHeight;
        }
        if (subCount >= 2)
        {
            DrawKdjPane(dc, startIdx, n, w, y, SubPaneHeight, spacing, kdj);
            y += SubPaneHeight;
        }
        if (subCount >= 3)
        {
            DrawRsiPane(dc, startIdx, n, w, y, SubPaneHeight, spacing, rsi);
        }
    }

    private void DrawPricePane(DrawingContext dc, List<decimal> points, double w, double mainH, bool showLabels)
    {
        var avgPoints = AvgPoints;
        bool drawAvg = showLabels && avgPoints != null && avgPoints.Count >= 2;

        double leftPad = showLabels ? 42 : 0;
        double topPad = showLabels ? 4 : 0;
        double bottomPad = showLabels ? 4 : 0;
        double chartW = Math.Max(1, w - leftPad);
        double chartH = Math.Max(1, mainH - topPad - bottomPad);

        var min = points.Min();
        var max = points.Max();
        var baseline = BaseLine;

        if (drawAvg)
        {
            var avgs = avgPoints!;
            min = Math.Min(min, avgs.Min());
            max = Math.Max(max, avgs.Max());
        }

        if (showLabels && baseline > 0)
        {
            if (baseline < min) min = baseline;
            if (baseline > max) max = baseline;
        }

        double range = (double)(max - min);
        if (range == 0) range = 1;

        double Y(decimal price) => topPad + chartH - (double)(price - min) / range * chartH;

        double baselineY = Math.Max(topPad, Math.Min(topPad + chartH, Y(baseline)));
        var baselinePen = new Pen(new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)), 0.5);
        dc.DrawLine(baselinePen, new Point(leftPad, baselineY), new Point(w, baselineY));

        if (showLabels)
        {
            var labelBrush = new SolidColorBrush(Color.FromArgb(140, 100, 100, 100));
            DrawLabel(dc, max.ToString("F2"), 2, topPad, labelBrush);
            DrawLabel(dc, min.ToString("F2"), 2, topPad + chartH - 12, labelBrush);

            // 顶部图例：均价 / 最新（对齐常见分时软件）
            decimal latest = points[^1];
            decimal? latestAvg = drawAvg && avgPoints!.Count > 0 ? avgPoints[^1] : null;
            double legendY = topPad;
            double lx = leftPad + 4;
            if (latestAvg.HasValue)
            {
                var avgBrush = new SolidColorBrush(Color.FromRgb(245, 166, 35));
                var avgFt = new FormattedText($"均价:{latestAvg.Value:F2}",
                    System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    LabelTypeface, 10, avgBrush, 1.0);
                dc.DrawText(avgFt, new Point(lx, legendY));
                lx += avgFt.Width + 10;
            }
            var priceBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            var priceFt = new FormattedText($"最新:{latest:F2}",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                LabelTypeface, 10, priceBrush, 1.0);
            dc.DrawText(priceFt, new Point(lx, legendY));
        }

        bool isUp = IsUp;
        // 详情分时：现价用深色折线，均价用黄色，避免红绿盖住均价
        Color lineColor = showLabels
            ? Color.FromRgb(0x22, 0x22, 0x22)
            : (isUp ? ColorRed : ColorGreen);
        var linePen = new Pen(new SolidColorBrush(lineColor), showLabels ? 1.4 : 1.0);
        double stepX = chartW / (points.Count - 1);

        DrawPolyline(dc, points, leftPad, stepX, Y, linePen);

        var fillGeometry = new StreamGeometry();
        using (var ctx = fillGeometry.Open())
        {
            ctx.BeginFigure(new Point(leftPad, Y(points[0])), true, true);
            var fillPoints = new List<Point>();
            for (int i = 1; i < points.Count; i++)
                fillPoints.Add(new Point(leftPad + i * stepX, Y(points[i])));
            ctx.PolyLineTo(fillPoints, true, false);
            ctx.LineTo(new Point(w, topPad + chartH), true, false);
            ctx.LineTo(new Point(leftPad, topPad + chartH), true, false);
        }
        Color fillC = showLabels
            ? Color.FromArgb(20, 100, 100, 100)
            : (isUp
                ? Color.FromArgb(30, ColorRed.R, ColorRed.G, ColorRed.B)
                : Color.FromArgb(30, ColorGreen.R, ColorGreen.G, ColorGreen.B));
        dc.DrawGeometry(new SolidColorBrush(fillC), null, fillGeometry);

        // 今日均价线（黄色，画在最上层）
        if (drawAvg)
        {
            int count = Math.Min(points.Count, avgPoints!.Count);
            if (count >= 2)
            {
                var avgSlice = avgPoints.Take(count).ToList();
                var avgPen = new Pen(new SolidColorBrush(Color.FromRgb(245, 166, 35)), 1.6);
                avgPen.Freeze();
                DrawPolyline(dc, avgSlice, leftPad, chartW / (count - 1), Y, avgPen);
            }
        }
    }

    private static List<KlineItem> ToPseudoKlines(List<decimal> prices, int roll = 9)
    {
        var list = new List<KlineItem>(prices.Count);
        for (int i = 0; i < prices.Count; i++)
        {
            int from = Math.Max(0, i - roll + 1);
            decimal high = prices[from], low = prices[from];
            for (int t = from + 1; t <= i; t++)
            {
                if (prices[t] > high) high = prices[t];
                if (prices[t] < low) low = prices[t];
            }
            decimal c = prices[i];
            list.Add(new KlineItem
            {
                Open = i > 0 ? prices[i - 1] : c,
                Close = c,
                High = Math.Max(high, c),
                Low = Math.Min(low, c),
                Volume = 1
            });
        }
        return list;
    }

    private void DrawMacdPane(DrawingContext dc, int startIdx, int n, double w, double top, double paneH,
        double spacing, IndicatorCalculator.MacdResult macd)
    {
        DrawPaneFrame(dc, w, top);
        int last = macd.Dif.Length - 1;
        DrawColoredLegend(dc, 8, top + 1, [
            ("MACD(12,26,9)", Colors.Gray),
            ($"DIF:{(macd.Dif[last]?.ToString("F3") ?? "-")}", ColorRed),
            ($"DEA:{(macd.Dea[last]?.ToString("F3") ?? "-")}", ColorGold),
            ($"M:{(macd.Hist[last]?.ToString("F3") ?? "-")}", ColorBlue)
        ]);

        double chartTop = top + 14;
        double chartBottom = top + paneH - 4;
        GetRange(macd.Dif, macd.Dea, macd.Hist, startIdx, n, out decimal hi, out decimal lo);
        if (hi < 0) hi = 0;
        if (lo > 0) lo = 0;
        double range = (double)(hi - lo);
        if (range == 0) range = 1;
        double Y(decimal v) => chartBottom - (double)(v - lo) / range * (chartBottom - chartTop);

        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)), 0.5),
            new Point(8, Y(0)), new Point(w - 4, Y(0)));

        double barW = Math.Max(1, spacing * 0.45);
        for (int i = 0; i < n; i++)
        {
            int gi = startIdx + i;
            if (!macd.Hist[gi].HasValue) continue;
            double x = 8 + (i + 0.5) * spacing;
            double y0 = Y(0);
            double y1 = Y(macd.Hist[gi]!.Value);
            bool up = macd.Hist[gi]!.Value >= 0;
            var brush = new SolidColorBrush(up
                ? Color.FromArgb(160, ColorRed.R, ColorRed.G, ColorRed.B)
                : Color.FromArgb(160, ColorGreen.R, ColorGreen.G, ColorGreen.B));
            dc.DrawRectangle(brush, null,
                new Rect(x - barW / 2, Math.Min(y0, y1), barW, Math.Max(1, Math.Abs(y1 - y0))));
        }

        DrawNullableLine(dc, macd.Dif, startIdx, n, spacing, Y, ColorRed, 1.1);
        DrawNullableLine(dc, macd.Dea, startIdx, n, spacing, Y, ColorGold, 1.1);
    }

    private void DrawKdjPane(DrawingContext dc, int startIdx, int n, double w, double top, double paneH,
        double spacing, IndicatorCalculator.KdjResult kdj)
    {
        DrawPaneFrame(dc, w, top);
        int last = kdj.K.Length - 1;
        DrawColoredLegend(dc, 8, top + 1, [
            ("KDJ(9,3,3)", Colors.Gray),
            ($"K:{(kdj.K[last]?.ToString("F2") ?? "-")}", ColorRed),
            ($"D:{(kdj.D[last]?.ToString("F2") ?? "-")}", ColorGold),
            ($"J:{(kdj.J[last]?.ToString("F2") ?? "-")}", ColorBlue)
        ]);

        double chartTop = top + 14;
        double chartBottom = top + paneH - 4;
        GetRange(kdj.K, kdj.D, kdj.J, startIdx, n, out decimal hi, out decimal lo);
        hi = Math.Max(hi, 100);
        lo = Math.Min(lo, 0);
        double range = (double)(hi - lo);
        if (range == 0) range = 1;
        double Y(decimal v) => chartBottom - (double)(v - lo) / range * (chartBottom - chartTop);

        DrawNullableLine(dc, kdj.K, startIdx, n, spacing, Y, ColorRed, 1.1);
        DrawNullableLine(dc, kdj.D, startIdx, n, spacing, Y, ColorGold, 1.1);
        DrawNullableLine(dc, kdj.J, startIdx, n, spacing, Y, ColorBlue, 1.1);
    }

    private void DrawRsiPane(DrawingContext dc, int startIdx, int n, double w, double top, double paneH,
        double spacing, IndicatorCalculator.RsiResult rsi)
    {
        DrawPaneFrame(dc, w, top);
        int last = rsi.Rsi1.Length - 1;
        DrawColoredLegend(dc, 8, top + 1, [
            ("RSI(6,12,24)", Colors.Gray),
            ($"R1:{(rsi.Rsi1[last]?.ToString("F2") ?? "-")}", ColorRed),
            ($"R2:{(rsi.Rsi2[last]?.ToString("F2") ?? "-")}", ColorGold),
            ($"R3:{(rsi.Rsi3[last]?.ToString("F2") ?? "-")}", ColorBlue)
        ]);

        double chartTop = top + 14;
        double chartBottom = top + paneH - 4;
        GetRange(rsi.Rsi1, rsi.Rsi2, rsi.Rsi3, startIdx, n, out decimal hi, out decimal lo);
        hi = Math.Max(hi, 100);
        lo = Math.Min(lo, 0);
        double range = (double)(hi - lo);
        if (range == 0) range = 1;
        double Y(decimal v) => chartBottom - (double)(v - lo) / range * (chartBottom - chartTop);

        DrawNullableLine(dc, rsi.Rsi1, startIdx, n, spacing, Y, ColorRed, 1.1);
        DrawNullableLine(dc, rsi.Rsi2, startIdx, n, spacing, Y, ColorGold, 1.1);
        DrawNullableLine(dc, rsi.Rsi3, startIdx, n, spacing, Y, ColorBlue, 1.1);
    }

    private static void DrawPaneFrame(DrawingContext dc, double w, double top)
    {
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), 0.5),
            new Point(8, top), new Point(w - 4, top));
    }

    private static void DrawNullableLine(DrawingContext dc, decimal?[] series, int startIdx, int n,
        double spacing, Func<decimal, double> yFunc, Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        Point? prev = null;
        for (int i = 0; i < n; i++)
        {
            int gi = startIdx + i;
            if (gi < 0 || gi >= series.Length || !series[gi].HasValue)
            {
                prev = null;
                continue;
            }
            var pt = new Point(8 + (i + 0.5) * spacing, yFunc(series[gi]!.Value));
            if (prev.HasValue) dc.DrawLine(pen, prev.Value, pt);
            prev = pt;
        }
    }

    private static void GetRange(decimal?[] a, decimal?[] b, decimal?[] c, int startIdx, int n,
        out decimal high, out decimal low)
    {
        high = decimal.MinValue;
        low = decimal.MaxValue;
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            int gi = startIdx + i;
            any |= Absorb(a, gi, ref high, ref low);
            any |= Absorb(b, gi, ref high, ref low);
            any |= Absorb(c, gi, ref high, ref low);
        }
        if (!any) { high = 1; low = 0; }
    }

    private static bool Absorb(decimal?[] arr, int gi, ref decimal high, ref decimal low)
    {
        if (gi < 0 || gi >= arr.Length || !arr[gi].HasValue) return false;
        decimal v = arr[gi]!.Value;
        if (v > high) high = v;
        if (v < low) low = v;
        return true;
    }

    private static void DrawColoredLegend(DrawingContext dc, double x, double y, (string Text, Color Color)[] items)
    {
        double cx = x;
        foreach (var (text, color) in items)
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, LabelTypeface, 9, new SolidColorBrush(color), 1.0);
            dc.DrawText(ft, new Point(cx, y));
            cx += ft.Width + 8;
        }
    }

    private static void DrawPolyline(DrawingContext dc, List<decimal> points, double leftPad, double stepX,
        Func<decimal, double> yFunc, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(leftPad, yFunc(points[0])), false, false);
            var polyPoints = new List<Point>();
            for (int i = 1; i < points.Count; i++)
                polyPoints.Add(new Point(leftPad + i * stepX, yFunc(points[i])));
            ctx.PolyLineTo(polyPoints, true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawLabel(DrawingContext dc, string text, double x, double y, SolidColorBrush brush)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, LabelTypeface, 10, brush, 1.0);
        dc.DrawText(ft, new Point(x, y));
    }
}
