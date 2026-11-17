using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StockTool.Core.Indicators;
using StockTool.Core.Models;

namespace StockTool.Controls;

public class KlineChartControl : Control
{
    private const double BaseHeight = 160;
    private const double SubPaneHeight = 78;
    private const int DefaultVisibleBars = 60;
    /// <summary>滚轮放大的最小可见根数</summary>
    private const int MinVisibleBars = 12;
    /// <summary>红利策略标记与 K 线的间距</summary>
    private const double StrategyMarkGap = 16;

    // 悬停十字光标：可见区索引 + 最近一次渲染的坐标映射
    private int _hoverIndex = -1;
    private HoverLayout _layout;
    // 滚轮缩放后的可见根数；0 表示用默认值
    private int _visibleBars;

    public static readonly DependencyProperty KlineDataProperty =
        DependencyProperty.Register(nameof(KlineData), typeof(List<KlineItem>), typeof(KlineChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDataChanged));

    public static readonly DependencyProperty VisibleSubCountProperty =
        DependencyProperty.Register(nameof(VisibleSubCount), typeof(int), typeof(KlineChartControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnSubCountChanged));

    /// <summary>红利策略买卖点（日/周/月 BOLL 共振），与短线 MACD+KDJ 标记分开显示</summary>
    public static readonly DependencyProperty StrategySignalsProperty =
        DependencyProperty.Register(nameof(StrategySignals), typeof(List<IndicatorCalculator.TradeSignal>),
            typeof(KlineChartControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrategyTextProperty =
        DependencyProperty.Register(nameof(StrategyText), typeof(string), typeof(KlineChartControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>当前周期（与 StockItem.KlinePeriod 一致），用于判断最后一根是否为正在形成的K线</summary>
    public static readonly DependencyProperty KlinePeriodProperty =
        DependencyProperty.Register(nameof(KlinePeriod), typeof(int), typeof(KlineChartControl),
            new FrameworkPropertyMetadata(101));

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

    public List<IndicatorCalculator.TradeSignal>? StrategySignals
    {
        get => (List<IndicatorCalculator.TradeSignal>?)GetValue(StrategySignalsProperty);
        set => SetValue(StrategySignalsProperty, value);
    }

    public string StrategyText
    {
        get => (string)GetValue(StrategyTextProperty);
        set => SetValue(StrategyTextProperty, value);
    }

    public int KlinePeriod
    {
        get => (int)GetValue(KlinePeriodProperty);
        set => SetValue(KlinePeriodProperty, value);
    }

    /// <summary>
    /// 盘中用现价刷新正在形成的那根 K 线，触发指标与买卖点重算重绘。
    /// 直接 InvalidateVisual，不重置悬停索引，避免十字光标闪烁。
    /// </summary>
    public void SyncLastBar(decimal price)
    {
        var data = KlineData;
        if (data == null || data.Count == 0 || price <= 0) return;

        var last = data[^1];
        if (!IsCurrentBar(last.Date, KlinePeriod)) return;
        if (last.Close == price) return;

        last.Close = price;
        if (price > last.High) last.High = price;
        if (price < last.Low) last.Low = price;

        InvalidateVisual();
    }

    // 最后一根是否属于当前正在形成的周期（日/分钟看当天，周线看本周，月线看本月）
    private static bool IsCurrentBar(DateTime barDate, int period)
    {
        var today = DateTime.Today;
        if (barDate.Date == today) return true;

        if (period == 102)
        {
            int offset = ((int)today.DayOfWeek + 6) % 7;
            var start = today.AddDays(-offset);
            return barDate.Date >= start && barDate.Date <= start.AddDays(6);
        }

        if (period == 103)
            return barDate.Year == today.Year && barDate.Month == today.Month;

        return false;
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
        // 透明背景才会走命中测试，才能接收鼠标移动
        Background = Brushes.Transparent;
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KlineChartControl chart)
        {
            chart._hoverIndex = -1;
            chart.UpdateHeight();
            chart.InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_layout.Count < 1 || _layout.StepX <= 0) return;

        var pos = e.GetPosition(this);
        int index = (int)Math.Round((pos.X - _layout.LeftPad) / _layout.StepX);
        index = Math.Clamp(index, 0, _layout.Count - 1);
        if (index == _hoverIndex) return;

        _hoverIndex = index;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex < 0) return;

        _hoverIndex = -1;
        InvalidateVisual();
    }

    /// <summary>滚轮缩放可见 K 线根数：上滚放大，下滚缩小</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var data = KlineData;
        if (data == null || data.Count <= MinVisibleBars) return;

        int total = data.Count;
        int current = Math.Min(_visibleBars > 0 ? _visibleBars : DefaultVisibleBars, total);
        int next = e.Delta > 0
            ? (int)Math.Round(current * 0.8)
            : (int)Math.Round(current * 1.25);
        next = Math.Clamp(next, MinVisibleBars, total);

        e.Handled = true;
        if (next == current && _visibleBars > 0) return;

        _visibleBars = next >= total ? total : next;
        _hoverIndex = -1;
        InvalidateVisual();
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

        // 可见根数受滚轮缩放控制
        int bars = _visibleBars > 0 ? _visibleBars : DefaultVisibleBars;
        int maxVisible = Math.Min(Math.Max(bars, MinVisibleBars), data.Count);
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
        var signals = IndicatorCalculator.MacdKdjSignals(data);

        DrawMainPane(dc, data, visible, startIdx, n, w, mainH, adjSpacing, adjCandleW, boll, signals);

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

        // 悬停十字光标：竖虚线贯穿主图与指标子图
        DrawHoverCrosshair(dc, data, w, h, mainH);
    }

    private void DrawMainPane(DrawingContext dc, List<KlineItem> full, List<KlineItem> visible, int startIdx,
        int n, double w, double mainH, double spacing, double candleW, IndicatorCalculator.BollResult boll,
        List<IndicatorCalculator.TradeSignal> signals)
    {
        double volH = mainH * 0.22;
        // 顶部留三行：BOLL 图例 + 买卖点摘要 + 红利策略摘要
        double candleH = mainH - volH - 36;
        double candleTop = 36;
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

        // 记录坐标映射，供鼠标悬停反查索引与绘制光标
        _layout = new HoverLayout
        {
            LeftPad = 8,
            StepX = spacing,
            Count = n,
            StartIdx = startIdx,
            PriceMin = allLow,
            PriceMax = allHigh,
            Top = candleTop,
            Bottom = candleBottom
        };

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

        // 第二行右侧：悬停时显示该根 K 线数据，否则显示最新买卖点建议
        if (!DrawHoverInfo(dc, full, w, 12))
            DrawSignalSummary(dc, signals, w, 12);

        // 第三行右侧：红利策略摘要（仅日线，由外部传入）
        DrawStrategySummary(dc, w, 24);

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

        // 买卖点标记（画在最上层，避免被蜡烛/BOLL 遮盖）
        DrawSignalMarkers(dc, visible, startIdx, n, spacing, Y, candleTop, candleBottom, signals);

        // 红利策略标记：金色「吸」在低位、「减」在高位，与短线买卖点错开
        DrawStrategyMarkers(dc, visible, startIdx, n, spacing, Y, candleTop, candleBottom);
    }

    // 红利策略标记：与短线标记错开，避免同柱重叠
    private void DrawStrategyMarkers(DrawingContext dc, List<KlineItem> visible, int startIdx, int n,
        double spacing, Func<decimal, double> y, double top, double bottom)
    {
        var signals = StrategySignals;
        if (signals == null || signals.Count == 0) return;

        var brush = new SolidColorBrush(ColorGold);
        foreach (var sig in signals)
        {
            int i = sig.Index - startIdx;
            if (i < 0 || i >= n) continue;

            var bar = visible[i];
            double x = 8 + (i + 0.5) * spacing;

            if (sig.IsBuy)
            {
                double textY = Math.Min(y(bar.Low) + StrategyMarkGap, bottom - 11);
                DrawText(dc, "吸", x - 5, textY, 9, brush);
            }
            else
            {
                double textY = Math.Max(y(bar.High) - StrategyMarkGap, top + 1);
                DrawText(dc, "减", x - 5, textY, 9, brush);
            }
        }
    }

    // 第三行右侧：红利策略摘要
    private void DrawStrategySummary(DrawingContext dc, double w, double y)
    {
        string text = StrategyText;
        if (string.IsNullOrEmpty(text)) return;

        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, DefaultTypeface, 9, new SolidColorBrush(ColorGold), 1.0);
        dc.DrawText(ft, new Point(Math.Max(8, w - 4 - ft.Width), y));
    }

    // 买点：K线下方红三角+「买」；卖点：K线上方绿三角+「卖」
    private void DrawSignalMarkers(DrawingContext dc, List<KlineItem> visible, int startIdx, int n,
        double spacing, Func<decimal, double> y, double top, double bottom,
        List<IndicatorCalculator.TradeSignal> signals)
    {
        foreach (var sig in signals)
        {
            int i = sig.Index - startIdx;
            if (i < 0 || i >= n) continue;

            var bar = visible[i];
            double x = 8 + (i + 0.5) * spacing;
            var brush = new SolidColorBrush(sig.IsBuy ? ColorRed : ColorGreen);

            if (sig.IsBuy)
            {
                double yBase = Math.Min(y(bar.Low) + 3, bottom - 8);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(x, yBase - 6), true, true);
                    ctx.LineTo(new Point(x - 3.2, yBase), true, false);
                    ctx.LineTo(new Point(x + 3.2, yBase), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(brush, null, geo);
                DrawText(dc, "买", x - 4, yBase + 1, 9, brush);
            }
            else
            {
                double yBase = Math.Max(y(bar.High) - 3, top + 8);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(x, yBase + 6), true, true);
                    ctx.LineTo(new Point(x - 3.2, yBase), true, false);
                    ctx.LineTo(new Point(x + 3.2, yBase), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(brush, null, geo);
                DrawText(dc, "卖", x - 4, yBase - 12, 9, brush);
            }
        }
    }

    // 顶部信号摘要：展示最新一条共振信号，无信号时提示观望
    private static void DrawSignalSummary(DrawingContext dc, List<IndicatorCalculator.TradeSignal> signals,
        double w, double y)
    {
        IndicatorCalculator.TradeSignal? latest = signals.Count > 0 ? signals[^1] : null;
        string text = latest == null
            ? "MACD+KDJ 暂无买卖点"
            : (latest.IsBuy ? "买点：" : "卖点：") + latest.Reason;
        Color color = latest == null
            ? Color.FromArgb(150, 100, 100, 100)
            : (latest.IsBuy ? ColorRed : ColorGreen);

        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, DefaultTypeface, 9, new SolidColorBrush(color), 1.0);
        dc.DrawText(ft, new Point(Math.Max(8, w - 4 - ft.Width), y));
    }

    // 悬停时第二行显示该根 K 线的开高低收，已绘制返回 true
    private bool DrawHoverInfo(DrawingContext dc, List<KlineItem> full, double w, double y)
    {
        if (!TryResolveHoverBar(full, out var bar)) return false;

        Color color = bar.Close >= bar.Open ? ColorRed : ColorGreen;
        string text = $"{FormatBarTime(bar.Date)}  开{bar.Open:F2} 高{bar.High:F2} 低{bar.Low:F2} 收{bar.Close:F2}";

        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, DefaultTypeface, 9, new SolidColorBrush(color), 1.0);
        dc.DrawText(ft, new Point(Math.Max(8, w - 4 - ft.Width), y));
        return true;
    }

    // 悬停十字光标：竖虚线贯穿主图与子图，横虚线为悬停收盘价
    private void DrawHoverCrosshair(DrawingContext dc, List<KlineItem> full, double w, double h, double mainH)
    {
        if (!TryResolveHoverBar(full, out var bar)) return;

        var layout = _layout;
        double x = layout.LeftPad + (_hoverIndex + 0.5) * layout.StepX;
        double range = (double)(layout.PriceMax - layout.PriceMin);
        if (range <= 0) range = 1;
        double y = layout.Bottom - (double)(bar.Close - layout.PriceMin) / range * (layout.Bottom - layout.Top);
        y = Math.Clamp(y, layout.Top, layout.Bottom);

        Color color = bar.Close >= bar.Open ? ColorRed : ColorGreen;

        CrosshairRenderer.DrawVerticalLine(dc, x, layout.Top, h);
        CrosshairRenderer.DrawHorizontalLine(dc, 8, w - 4, y);
        CrosshairRenderer.DrawPoint(dc, x, y, color);
        CrosshairRenderer.DrawTag(dc, bar.Close.ToString("F2"), y, w - 2, color);
        CrosshairRenderer.DrawTimeTag(dc, FormatBarTime(bar.Date), x, mainH - 8, 8, w);
    }

    // 悬停索引 → 该根 K 线
    private bool TryResolveHoverBar(List<KlineItem> full, out KlineItem bar)
    {
        bar = null!;
        if (_hoverIndex < 0 || _hoverIndex >= _layout.Count) return false;

        int gi = _layout.StartIdx + _hoverIndex;
        if (gi < 0 || gi >= full.Count) return false;

        bar = full[gi];
        return true;
    }

    // 分钟线带时间，日线/周线只显示日期
    private static string FormatBarTime(DateTime time) =>
        time.TimeOfDay == TimeSpan.Zero ? time.ToString("yyyy-MM-dd") : time.ToString("MM-dd HH:mm");

    /// <summary>最近一次渲染的坐标映射，供鼠标悬停反查索引</summary>
    private readonly struct HoverLayout
    {
        public double LeftPad { get; init; }
        public double StepX { get; init; }
        public int Count { get; init; }
        public int StartIdx { get; init; }
        public decimal PriceMin { get; init; }
        public decimal PriceMax { get; init; }
        public double Top { get; init; }
        public double Bottom { get; init; }
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
