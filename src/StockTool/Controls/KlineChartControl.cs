using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StockTool.Core.Indicators;
using StockTool.Core.Models;

namespace StockTool.Controls;

public class KlineChartControl : Control
{
    private const double BaseHeight = 160;
    private const double SubPaneHeight = 78;
    private const int DefaultVisibleBars = 60;

    public static readonly DependencyProperty KlineDataProperty =
        DependencyProperty.Register(nameof(KlineData), typeof(List<KlineItem>), typeof(KlineChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDataChanged));

    public static readonly DependencyProperty VisibleSubCountProperty =
        DependencyProperty.Register(nameof(VisibleSubCount), typeof(int), typeof(KlineChartControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnSubCountChanged));

    public List<KlineItem>? KlineData
    {
        get => (List<KlineItem>?)GetValue(KlineDataProperty);
        set => SetValue(KlineDataProperty, value);
    }

    /// <summary>0=仅主图，1=+MACD，2=+KDJ，3=+RSI</summary>
    public int VisibleSubCount
    {
        get => (int)GetValue(VisibleSubCountProperty);
        set => SetValue(VisibleSubCountProperty, value);
    }

    private static readonly Typeface DefaultTypeface = new("Microsoft YaHei");

    private static readonly Color ColorRed = Color.FromRgb(217, 48, 37);
    private static readonly Color ColorGreen = Color.FromRgb(26, 140, 63);
    private static readonly Color ColorGold = Color.FromRgb(196, 140, 60);
    private static readonly Color ColorBlue = Color.FromRgb(64, 120, 200);
    private static readonly Color ColorMid = Color.FromRgb(217, 48, 37);

    static KlineChartControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(KlineChartControl),
            new FrameworkPropertyMetadata(typeof(KlineChartControl)));
    }

    public KlineChartControl()
    {
        Height = BaseHeight;
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KlineChartControl chart)
        {
            chart.UpdateHeight();
            chart.InvalidateVisual();
        }
    }

    private static void OnSubCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KlineChartControl chart)
        {
            chart.UpdateHeight();
            chart.InvalidateVisual();
        }
    }

    private void UpdateHeight()
    {
        Height = BaseHeight + Math.Clamp(VisibleSubCount, 0, 3) * SubPaneHeight;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var data = KlineData;
        if (data == null || data.Count < 2) return;

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        int subCount = Math.Clamp(VisibleSubCount, 0, 3);
        double subTotal = subCount * SubPaneHeight;
        double mainH = Math.Max(80, h - subTotal);

        int maxVisible = Math.Clamp(DefaultVisibleBars, 20, data.Count);
        int startIdx = Math.Max(0, data.Count - maxVisible);
        var visible = data.Skip(startIdx).ToList();
        int n = visible.Count;
        if (n < 2) return;

        double adjSpacing = (w - 16) / (n + 0.5);
        double adjCandleW = Math.Max(1.5, adjSpacing * 0.65);

        var boll = IndicatorCalculator.Boll(data);
        var macd = IndicatorCalculator.Macd(data);
        var kdj = IndicatorCalculator.Kdj(data);
        var rsi = IndicatorCalculator.Rsi(data);

        DrawMainPane(dc, data, visible, startIdx, n, w, mainH, adjSpacing, adjCandleW, boll);

        double y = mainH;
        if (subCount >= 1)
        {
            DrawMacdPane(dc, startIdx, n, w, y, SubPaneHeight, adjSpacing, macd);
            y += SubPaneHeight;
        }
        if (subCount >= 2)
        {
            DrawKdjPane(dc, startIdx, n, w, y, SubPaneHeight, adjSpacing, kdj);
            y += SubPaneHeight;
        }
        if (subCount >= 3)
        {
            DrawRsiPane(dc, startIdx, n, w, y, SubPaneHeight, adjSpacing, rsi);
        }
    }

    private void DrawMainPane(DrawingContext dc, List<KlineItem> full, List<KlineItem> visible, int startIdx,
        int n, double w, double mainH, double spacing, double candleW, IndicatorCalculator.BollResult boll)
    {
        double volH = mainH * 0.22;
        double candleH = mainH - volH - 14;
        double candleTop = 14;
        double candleBottom = candleH;

        decimal allHigh = visible.Max(d => d.High);
        decimal allLow = visible.Min(d => d.Low);
        for (int i = 0; i < n; i++)
        {
            int gi = startIdx + i;
            ExpandRange(boll.Upper[gi], ref allHigh, ref allLow);
            ExpandRange(boll.Lower[gi], ref allHigh, ref allLow);
            ExpandRange(boll.Mid[gi], ref allHigh, ref allLow);
        }

        double priceRange = (double)(allHigh - allLow);
        if (priceRange == 0) priceRange = 1;

        double Y(decimal price) =>
            candleBottom - (double)(price - allLow) / priceRange * (candleBottom - candleTop);

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), 0.5);
        dc.DrawLine(gridPen, new Point(8, candleTop), new Point(w - 4, candleTop));
        dc.DrawLine(gridPen, new Point(8, candleBottom), new Point(w - 4, candleBottom));

        var labelBrush = new SolidColorBrush(Color.FromArgb(140, 100, 100, 100));
        DrawText(dc, allHigh.ToString("F2"), 2, candleTop - 1, 10, labelBrush);
        DrawText(dc, allLow.ToString("F2"), 2, candleBottom - 11, 10, labelBrush);

        // legend
        int last = full.Count - 1;
        string midTxt = boll.Mid[last]?.ToString("F3") ?? "-";
        string ubTxt = boll.Upper[last]?.ToString("F3") ?? "-";
        string lbTxt = boll.Lower[last]?.ToString("F3") ?? "-";
        DrawColoredLegend(dc, 8, 1, [
            ("BOLL(20)", Colors.Gray),
            ($"MID:{midTxt}", ColorMid),
            ($"UB:{ubTxt}", ColorGold),
            ($"LB:{lbTxt}", ColorBlue)
        ]);

        // candles + volume
        var maxVolume = visible.Max(d => d.Volume);
        if (maxVolume <= 0) maxVolume = 1;
        double volTop = candleBottom + 2;
        double volBottom = mainH - 2;
        double volRange = Math.Max(1, volBottom - volTop);
        dc.DrawLine(gridPen, new Point(8, volTop), new Point(w - 4, volTop));

        for (int i = 0; i < n; i++)
        {
            var item = visible[i];
            double x = 8 + (i + 0.5) * spacing;
            bool isUp = item.Close >= item.Open;
            Color candleColor = isUp ? ColorRed : ColorGreen;

            double openY = Y(item.Open);
            double closeY = Y(item.Close);
            double highY = Y(item.High);
            double lowY = Y(item.Low);
            double bodyTop = Math.Min(openY, closeY);
            double bodyH = Math.Max(1, Math.Abs(closeY - openY));

            var candlePen = new Pen(new SolidColorBrush(candleColor), 1.0);
            var candleBrush = new SolidColorBrush(isUp ? ColorRed : Color.FromArgb(0, 0, 0, 0));
            dc.DrawLine(candlePen, new Point(x, highY), new Point(x, lowY));
            double halfW = candleW / 2;
            dc.DrawRectangle(candleBrush, candlePen, new Rect(x - halfW, bodyTop, candleW, bodyH));

            double barH = (double)(item.Volume / maxVolume) * volRange;
            var volBrush = new SolidColorBrush(isUp
                ? Color.FromArgb(80, ColorRed.R, ColorRed.G, ColorRed.B)
                : Color.FromArgb(80, ColorGreen.R, ColorGreen.G, ColorGreen.B));
            dc.DrawRectangle(volBrush, null, new Rect(x - halfW, volBottom - barH, candleW, Math.Max(1, barH)));
        }

        // BOLL lines
        DrawNullableLine(dc, boll.Mid, startIdx, n, spacing, Y, ColorMid, 1.2);
        DrawNullableLine(dc, boll.Upper, startIdx, n, spacing, Y, ColorGold, 1.0);
        DrawNullableLine(dc, boll.Lower, startIdx, n, spacing, Y, ColorBlue, 1.0);
    }

    private void DrawMacdPane(DrawingContext dc, int startIdx, int n, double w, double top, double paneH,
        double spacing, IndicatorCalculator.MacdResult macd)
    {
        DrawPaneFrame(dc, w, top, paneH);

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
        // include 0
        if (hi < 0) hi = 0;
        if (lo > 0) lo = 0;
        double range = (double)(hi - lo);
        if (range == 0) range = 1;

        double Y(decimal v) => chartBottom - (double)(v - lo) / range * (chartBottom - chartTop);

        // zero line
        var zeroPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)), 0.5);
        dc.DrawLine(zeroPen, new Point(8, Y(0)), new Point(w - 4, Y(0)));

        double barW = Math.Max(1, spacing * 0.45);
        for (int i = 0; i < n; i++)
        {
            int gi = startIdx + i;
            if (!macd.Hist[gi].HasValue) continue;
            double x = 8 + (i + 0.5) * spacing;
            double y0 = Y(0);
            double y1 = Y(macd.Hist[gi]!.Value);
            bool up = macd.Hist[gi]!.Value >= 0;
            var brush = new SolidColorBrush(up ? Color.FromArgb(160, ColorRed.R, ColorRed.G, ColorRed.B)
                : Color.FromArgb(160, ColorGreen.R, ColorGreen.G, ColorGreen.B));
            double topY = Math.Min(y0, y1);
            double h = Math.Max(1, Math.Abs(y1 - y0));
            dc.DrawRectangle(brush, null, new Rect(x - barW / 2, topY, barW, h));
        }

        DrawNullableLine(dc, macd.Dif, startIdx, n, spacing, Y, ColorRed, 1.1);
        DrawNullableLine(dc, macd.Dea, startIdx, n, spacing, Y, ColorGold, 1.1);
    }

    private void DrawKdjPane(DrawingContext dc, int startIdx, int n, double w, double top, double paneH,
        double spacing, IndicatorCalculator.KdjResult kdj)
    {
        DrawPaneFrame(dc, w, top, paneH);
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
        DrawPaneFrame(dc, w, top, paneH);
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

    private static void DrawPaneFrame(DrawingContext dc, double w, double top, double paneH)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), 0.5);
        dc.DrawLine(pen, new Point(8, top), new Point(w - 4, top));
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
            if (prev.HasValue)
                dc.DrawLine(pen, prev.Value, pt);
            prev = pt;
        }
    }

    private static void ExpandRange(decimal? v, ref decimal high, ref decimal low)
    {
        if (!v.HasValue) return;
        if (v.Value > high) high = v.Value;
        if (v.Value < low) low = v.Value;
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

    private void DrawColoredLegend(DrawingContext dc, double x, double y, (string Text, Color Color)[] items)
    {
        double cx = x;
        foreach (var (text, color) in items)
        {
            var brush = new SolidColorBrush(color);
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, DefaultTypeface, 9, brush, 1.0);
            dc.DrawText(ft, new Point(cx, y));
            cx += ft.Width + 8;
        }
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, double fontSize,
        SolidColorBrush brush)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, DefaultTypeface, fontSize, brush, 1.0);
        dc.DrawText(ft, new Point(x, y));
    }
}
