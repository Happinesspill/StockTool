using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StockTool.Core.Indicators;
using StockTool.Core.Models;

namespace StockTool.Controls;

public class SparklineControl : Control
{
    private const double BaseHeight = 196;
    private const double SubPaneHeight = 78;
    /// <summary>详情分时顶部图例（均价/最新）占用高度，避免折线高点压住文字</summary>
    private const double LegendHeight = 16;
    /// <summary>买卖点摘要行高度</summary>
    private const double SignalRowHeight = 13;
    /// <summary>买卖点标记与价格线的垂直间距</summary>
    private const double TradeMarkGap = 22;

    // 悬停十字光标状态：索引 + 最近一次渲染的坐标映射
    private int _hoverIndex = -1;
    private HoverLayout _layout;
    // 滚轮缩放后的显示点数；0 表示显示全部
    private int _zoomCount;

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(List<decimal>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    public static readonly DependencyProperty AvgPointsProperty =
        DependencyProperty.Register(nameof(AvgPoints), typeof(List<decimal>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    public static readonly DependencyProperty TimesProperty =
        DependencyProperty.Register(nameof(Times), typeof(List<string>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    public static readonly DependencyProperty WarmupPointsProperty =
        DependencyProperty.Register(nameof(WarmupPoints), typeof(List<decimal>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

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

    public static readonly DependencyProperty ExpectedCountProperty =
        DependencyProperty.Register(nameof(ExpectedCount), typeof(int), typeof(SparklineControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

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

    /// <summary>分时时间点（HH:mm），与 Points 等长，供十字光标显示</summary>
    public List<string>? Times
    {
        get => (List<string>?)GetValue(TimesProperty);
        set => SetValue(TimesProperty, value);
    }

    /// <summary>上一交易日分钟价，仅用于指标预热，让子图从当日第一个点就有值</summary>
    public List<decimal>? WarmupPoints
    {
        get => (List<decimal>?)GetValue(WarmupPointsProperty);
        set => SetValue(WarmupPointsProperty, value);
    }

    /// <summary>
    /// 盘中用现价刷新最后一个分时点，触发买卖点重算重绘。
    /// 直接 InvalidateVisual，不重置悬停索引，避免十字光标闪烁。
    /// </summary>
    public void SyncLastPrice(decimal price)
    {
        var points = Points;
        if (points == null || points.Count == 0 || price <= 0) return;
        if (points[^1] == price) return;

        points[^1] = price;
        InvalidateVisual();
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

    // 全日固定点数；>0 时按槽位向后绘制，未到收盘右侧留白
    public int ExpectedCount
    {
        get => (int)GetValue(ExpectedCountProperty);
        set => SetValue(ExpectedCountProperty, value);
    }

    static SparklineControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SparklineControl),
            new FrameworkPropertyMetadata(typeof(SparklineControl)));
    }

    public SparklineControl()
    {
        UpdateHitTest();
    }

    private static void OnShowLabelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SparklineControl c)
        {
            c.UpdateHeight();
            c.UpdateHitTest();
        }
    }

    /// <summary>数据刷新后索引可能失效，重置悬停状态</summary>
    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SparklineControl c) return;
        c._hoverIndex = -1;
        c.InvalidateVisual();
    }

    /// <summary>必须透明背景才会走 OnRender；列表小图不接收鼠标</summary>
    private void UpdateHitTest()
    {
        Background = Brushes.Transparent;
        IsHitTestVisible = ShowLabels;
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!ShowLabels || _layout.Count < 2 || _layout.StepX <= 0) return;

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

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var allPoints = Points;
        if (allPoints == null || allPoints.Count < 2) return;

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        bool showLabels = ShowLabels;
        int subCount = showLabels ? Math.Clamp(VisibleSubCount, 0, 3) : 0;
        double subTotal = subCount * SubPaneHeight;
        double mainH = Math.Max(40, h - subTotal);

        // 滚轮缩放：只显示最近 visibleCount 个点，未缩放时保持原有右侧留白
        int total = allPoints.Count;
        int visibleCount = _zoomCount <= 0 ? total : Math.Min(Math.Max(_zoomCount, MinZoomPoints), total);
        int viewStart = total - visibleCount;
        bool zoomed = visibleCount < total;

        var points = zoomed ? allPoints.GetRange(viewStart, visibleCount) : allPoints;
        var avgPoints = SliceWindow(AvgPoints, viewStart, visibleCount);
        var times = SliceWindow(Times, viewStart, visibleCount);
        // 主图与子图共用槽位数，缩放时不再保留盘中留白
        int slots = zoomed ? visibleCount : ResolveSlotCount(total);

        // 详情分时：用分钟数据构造伪K线，按 MACD+KDJ 共振判定当日买卖点
        // 预热：拼接上一交易日分钟价，让 MACD/KDJ/RSI 从当日第一个点起就有值
        List<KlineItem>? bars = null;
        List<IndicatorCalculator.TradeSignal>? signals = null;
        int warmupCount = 0;
        if (showLabels)
        {
            var source = allPoints;
            if (WarmupPoints is { Count: > 0 } warmup)
            {
                var merged = new List<decimal>(warmup.Count + allPoints.Count);
                merged.AddRange(warmup);
                merged.AddRange(allPoints);
                source = merged;
                warmupCount = warmup.Count;
            }

            bars = ToPseudoKlines(source);
            signals = IndicatorCalculator.MacdKdjSignals(bars);
            if (warmupCount > 0)
                signals = ShiftSignals(signals, warmupCount, total);
            if (zoomed)
                signals = ShiftSignals(signals, viewStart, visibleCount);
        }

        DrawPricePane(dc, points, avgPoints, times, w, mainH, showLabels, signals, slots);

        if (subCount <= 0) return;

        // 复用上面已构造的伪 K 线，供 KDJ 等指标计算
        var kbars = bars ?? ToPseudoKlines(allPoints);
        var macd = IndicatorCalculator.Macd(kbars);
        var kdj = IndicatorCalculator.Kdj(kbars);
        var rsi = IndicatorCalculator.Rsi(kbars);

        int n = visibleCount;
        // 子图只画窗口内部分，索引跳过预热段与缩放窗口
        int startIdx = warmupCount + viewStart;
        // 子图沿用主图时间轴（左侧标签留白 + 同一步长），保证数据点与悬停竖线垂直对齐
        double axisLeft = PriceAxisLeftPad;
        double axisStep = Math.Max(1, w - PriceAxisLeftPad) / Math.Max(1, slots - 1);

        double y = mainH;
        if (subCount >= 1)
        {
            DrawMacdPane(dc, startIdx, n, w, y, SubPaneHeight, axisLeft, axisStep, macd);
            y += SubPaneHeight;
        }
        if (subCount >= 2)
        {
            DrawKdjPane(dc, startIdx, n, w, y, SubPaneHeight, axisLeft, axisStep, kdj);
            y += SubPaneHeight;
        }
        if (subCount >= 3)
        {
            DrawRsiPane(dc, startIdx, n, w, y, SubPaneHeight, axisLeft, axisStep, rsi);
        }

        // 悬停竖虚线同步延伸到下方指标子图
        if (_hoverIndex >= 0 && _hoverIndex < n)
        {
            double hoverX = axisLeft + _hoverIndex * axisStep;
            CrosshairRenderer.DrawVerticalLine(dc, hoverX, mainH, mainH + subCount * SubPaneHeight);
        }
    }

    /// <summary>主图左侧价格标签留白宽度</summary>
    private const double PriceAxisLeftPad = 42;
    /// <summary>滚轮放大后最少显示的点数</summary>
    private const int MinZoomPoints = 20;

    // 丢弃窗口外信号，索引改为窗口相对位置
    private static List<IndicatorCalculator.TradeSignal> ShiftSignals(
        List<IndicatorCalculator.TradeSignal> signals, int offset, int count)
    {
        var result = new List<IndicatorCalculator.TradeSignal>();
        foreach (var sig in signals)
        {
            if (sig.Index < offset || sig.Index >= offset + count) continue;
            result.Add(new IndicatorCalculator.TradeSignal
            {
                Index = sig.Index - offset,
                IsBuy = sig.IsBuy,
                Reason = sig.Reason
            });
        }
        return result;
    }

    // 按窗口截取序列；整段可见时直接返回原引用
    private static List<T>? SliceWindow<T>(List<T>? source, int start, int count)
    {
        if (source == null || source.Count == 0) return source;
        if (start == 0 && count >= source.Count) return source;
        if (start >= source.Count) return null;

        return source.GetRange(start, Math.Min(count, source.Count - start));
    }

    /// <summary>滚轮缩放分时显示范围：上滚放大，下滚缩小</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // 列表/任务栏小图不参与缩放
        if (!ShowLabels) return;
        var all = Points;
        if (all == null || all.Count <= MinZoomPoints) return;

        int total = all.Count;
        int current = _zoomCount <= 0 || _zoomCount > total ? total : _zoomCount;
        int next = e.Delta > 0
            ? (int)Math.Round(current * 0.8)
            : (int)Math.Round(current * 1.25);
        next = Math.Clamp(next, MinZoomPoints, total);

        e.Handled = true;
        _zoomCount = next >= total ? 0 : next;
        _hoverIndex = -1;
        InvalidateVisual();
    }

    private int ResolveSlotCount(int pointCount)
    {
        int expected = ExpectedCount;
        if (expected > 1)
            return Math.Max(expected, pointCount);
        return Math.Max(pointCount, 2);
    }

    private void DrawPricePane(DrawingContext dc, List<decimal> points, List<decimal>? avgPoints,
        List<string>? times, double w, double mainH, bool showLabels,
        List<IndicatorCalculator.TradeSignal>? signals, int slots)
    {
        bool drawAvg = showLabels && avgPoints != null && avgPoints.Count >= 2;

        double leftPad = showLabels ? PriceAxisLeftPad : 0;
        // 列表/任务栏小图留 2px 上下边；详情图为图例与买卖点摘要预留顶部空间
        double topPad = showLabels ? LegendHeight + SignalRowHeight + 4 : 2;
        double bottomPad = showLabels ? 4 : 2;
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

            // 顶部图例画在绘图区上方留白内，悬停显示光标时点，否则显示最新值
            double legendY = 2;
            double lx = leftPad + 4;
            if (_hoverIndex >= 0 && _hoverIndex < points.Count)
            {
                DrawHoverLegend(dc, points, avgPoints, times, drawAvg, _hoverIndex, lx, legendY);
            }
            else
            {
                decimal latest = points[^1];
                decimal? latestAvg = drawAvg && avgPoints!.Count > 0 ? avgPoints[^1] : null;
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

            // 第二行右侧：最新买卖点建议
            DrawSignalSummary(dc, signals, w, LegendHeight + 2);
        }

        bool isUp = IsUp;
        // 详情分时：现价用深色折线，均价用黄色，避免红绿盖住均价
        Color lineColor = showLabels
            ? Color.FromRgb(0x22, 0x22, 0x22)
            : (isUp ? ColorRed : ColorGreen);
        // 统一 1.0 线宽，详情图线太粗会压住买卖点标记
        var linePen = new Pen(new SolidColorBrush(lineColor), 1.0);
        double stepX = chartW / Math.Max(1, slots - 1);
        double endX = leftPad + (points.Count - 1) * stepX;

        DrawPolyline(dc, points, leftPad, stepX, Y, linePen);

        var fillGeometry = new StreamGeometry();
        using (var ctx = fillGeometry.Open())
        {
            ctx.BeginFigure(new Point(leftPad, Y(points[0])), true, true);
            var fillPoints = new List<Point>();
            for (int i = 1; i < points.Count; i++)
                fillPoints.Add(new Point(leftPad + i * stepX, Y(points[i])));
            ctx.PolyLineTo(fillPoints, true, false);
            ctx.LineTo(new Point(endX, topPad + chartH), true, false);
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
                // 均价线也略收细，与现价线协调
                var avgPen = new Pen(new SolidColorBrush(Color.FromRgb(245, 166, 35)), 1.2);
                avgPen.Freeze();
                DrawPolyline(dc, avgSlice, leftPad, stepX, Y, avgPen);
            }
        }

        // 买卖点标记：买点在折线下方，卖点在折线上方
        if (signals != null && signals.Count > 0)
        {
            double plotBottom = topPad + chartH;
            foreach (var sig in signals)
            {
                int i = sig.Index;
                if (i < 0 || i >= points.Count) continue;

                double x = leftPad + i * stepX;
                double py = Y(points[i]);
                var brush = new SolidColorBrush(sig.IsBuy ? ColorRed : ColorGreen);

                // 标记与价格线保持 14px 间距，提高可读性
                if (sig.IsBuy)
                {
                    double yBase = Math.Min(py + TradeMarkGap, plotBottom - 14);
                    DrawTradeArrow(dc, x, yBase, true, brush);
                    DrawLabel(dc, "买", x - 4, yBase + 1, brush);
                }
                else
                {
                    double yBase = Math.Max(py - TradeMarkGap, topPad + 12);
                    DrawTradeArrow(dc, x, yBase, false, brush);
                    DrawLabel(dc, "卖", x - 4, yBase - 12, brush);
                }
            }
        }

        // 记录本次坐标映射，供鼠标悬停反查索引
        _layout = new HoverLayout
        {
            LeftPad = leftPad,
            StepX = stepX,
            Count = points.Count
        };

        // 悬停十字光标（仅详情大图）
        if (showLabels && _hoverIndex >= 0 && _hoverIndex < points.Count)
            DrawCrosshair(dc, points, avgPoints, times, drawAvg, _hoverIndex, leftPad, stepX, w, Y, topPad, chartH);
    }

    /// <summary>悬停数据行：时间 / 价 / 均价 / 涨跌幅</summary>
    private void DrawHoverLegend(DrawingContext dc, List<decimal> points, List<decimal>? avgPoints,
        List<string>? times, bool drawAvg, int index, double x, double y)
    {
        decimal price = points[index];
        decimal baseline = BaseLine;
        decimal change = baseline > 0 ? (price - baseline) / baseline * 100m : 0;
        string time = times != null && index < times.Count ? times[index] : string.Empty;

        var items = new List<(string Text, Color Color)>();
        if (!string.IsNullOrEmpty(time))
            items.Add((time, Color.FromRgb(0x33, 0x33, 0x33)));
        items.Add(($"价:{price:F2}", Color.FromRgb(0x33, 0x33, 0x33)));
        if (drawAvg && avgPoints != null && index < avgPoints.Count)
            items.Add(($"均:{avgPoints[index]:F2}", Color.FromRgb(245, 166, 35)));
        items.Add(($"{change:+0.00;-0.00;0.00}%", change >= 0 ? ColorRed : ColorGreen));

        DrawColoredLegend(dc, x, y, [.. items]);
    }

    /// <summary>十字光标：虚线 + 光标点 + 价格/涨幅/时间标签</summary>
    private void DrawCrosshair(DrawingContext dc, List<decimal> points, List<decimal>? avgPoints,
        List<string>? times, bool drawAvg, int index, double leftPad, double stepX, double w,
        Func<decimal, double> y, double topPad, double chartH)
    {
        decimal price = points[index];
        double x = leftPad + index * stepX;
        double priceY = y(price);
        double bottom = topPad + chartH;

        decimal baseline = BaseLine;
        decimal change = baseline > 0 ? (price - baseline) / baseline * 100m : 0;
        Color color = change >= 0 ? ColorRed : ColorGreen;

        CrosshairRenderer.DrawCrossLines(dc, x, priceY, leftPad, w, topPad, bottom);
        CrosshairRenderer.DrawPoint(dc, x, priceY, color);
        if (drawAvg && avgPoints != null && index < avgPoints.Count)
            CrosshairRenderer.DrawPoint(dc, x, y(avgPoints[index]), Color.FromRgb(245, 166, 35));

        CrosshairRenderer.DrawTag(dc, price.ToString("F2"), priceY, leftPad - 2, color);
        CrosshairRenderer.DrawTag(dc, $"{change:+0.00;-0.00;0.00}%", priceY, w - 2, color);

        string time = times != null && index < times.Count ? times[index] : string.Empty;
        CrosshairRenderer.DrawTimeTag(dc, time, x, bottom - 8, leftPad, w);
    }

    /// <summary>最近一次渲染的坐标映射，供鼠标悬停反查索引</summary>
    private readonly struct HoverLayout
    {
        public double LeftPad { get; init; }
        public double StepX { get; init; }
        public int Count { get; init; }
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
        double leftPad, double stepX, IndicatorCalculator.MacdResult macd)
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

        double barW = Math.Max(1, stepX * 0.45);
        for (int i = 0; i < n; i++)
        {
            int gi = startIdx + i;
            if (!macd.Hist[gi].HasValue) continue;
            double x = leftPad + i * stepX;
            double y0 = Y(0);
            double y1 = Y(macd.Hist[gi]!.Value);
            bool up = macd.Hist[gi]!.Value >= 0;
            var brush = new SolidColorBrush(up
                ? Color.FromArgb(160, ColorRed.R, ColorRed.G, ColorRed.B)
                : Color.FromArgb(160, ColorGreen.R, ColorGreen.G, ColorGreen.B));
            dc.DrawRectangle(brush, null,
                new Rect(x - barW / 2, Math.Min(y0, y1), barW, Math.Max(1, Math.Abs(y1 - y0))));
        }

        DrawNullableLine(dc, macd.Dif, startIdx, n, leftPad, stepX, Y, ColorRed, 1.1);
        DrawNullableLine(dc, macd.Dea, startIdx, n, leftPad, stepX, Y, ColorGold, 1.1);
    }

    private void DrawKdjPane(DrawingContext dc, int startIdx, int n, double w, double top, double paneH,
        double leftPad, double stepX, IndicatorCalculator.KdjResult kdj)
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

        DrawNullableLine(dc, kdj.K, startIdx, n, leftPad, stepX, Y, ColorRed, 1.1);
        DrawNullableLine(dc, kdj.D, startIdx, n, leftPad, stepX, Y, ColorGold, 1.1);
        DrawNullableLine(dc, kdj.J, startIdx, n, leftPad, stepX, Y, ColorBlue, 1.1);
    }

    private void DrawRsiPane(DrawingContext dc, int startIdx, int n, double w, double top, double paneH,
        double leftPad, double stepX, IndicatorCalculator.RsiResult rsi)
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

        DrawNullableLine(dc, rsi.Rsi1, startIdx, n, leftPad, stepX, Y, ColorRed, 1.1);
        DrawNullableLine(dc, rsi.Rsi2, startIdx, n, leftPad, stepX, Y, ColorGold, 1.1);
        DrawNullableLine(dc, rsi.Rsi3, startIdx, n, leftPad, stepX, Y, ColorBlue, 1.1);
    }

    private static void DrawPaneFrame(DrawingContext dc, double w, double top)
    {
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), 0.5),
            new Point(8, top), new Point(w - 4, top));
    }

    private static void DrawNullableLine(DrawingContext dc, decimal?[] series, int startIdx, int n,
        double leftPad, double stepX, Func<decimal, double> yFunc, Color color, double thickness)
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
            var pt = new Point(leftPad + i * stepX, yFunc(series[gi]!.Value));
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

    // 买卖点三角箭头：up=true 向上（买点），否则向下（卖点）
    private static void DrawTradeArrow(DrawingContext dc, double x, double yBase, bool up, SolidColorBrush brush)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (up)
            {
                ctx.BeginFigure(new Point(x, yBase - 6), true, true);
                ctx.LineTo(new Point(x - 3.2, yBase), true, false);
                ctx.LineTo(new Point(x + 3.2, yBase), true, false);
            }
            else
            {
                ctx.BeginFigure(new Point(x, yBase + 6), true, true);
                ctx.LineTo(new Point(x - 3.2, yBase), true, false);
                ctx.LineTo(new Point(x + 3.2, yBase), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }

    // 买卖点摘要：展示最新一条共振信号，无信号时提示观望
    private static void DrawSignalSummary(DrawingContext dc, List<IndicatorCalculator.TradeSignal>? signals,
        double w, double y)
    {
        IndicatorCalculator.TradeSignal? latest = signals is { Count: > 0 } ? signals[^1] : null;
        string text = latest == null
            ? "MACD+KDJ 暂无买卖点"
            : (latest.IsBuy ? "买点：" : "卖点：") + latest.Reason;
        Color color = latest == null
            ? Color.FromArgb(150, 100, 100, 100)
            : (latest.IsBuy ? ColorRed : ColorGreen);

        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, LabelTypeface, 9, new SolidColorBrush(color), 1.0);
        dc.DrawText(ft, new Point(Math.Max(46, w - 4 - ft.Width), y));
    }
}
