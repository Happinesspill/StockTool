using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StockTool.Core.Models;

namespace StockTool.Controls;

public class FundNavChartControl : Control
{
    private static readonly Typeface LabelTypeface = new("Microsoft YaHei");
    private static readonly Color ColorRed = Color.FromRgb(217, 48, 37);
    private static readonly Color ColorGreen = Color.FromRgb(26, 140, 63);
    private static readonly Color ColorGrid = Color.FromRgb(0xE8, 0xEC, 0xF1);
    private static readonly Color ColorAxis = Color.FromRgb(0x9A, 0xA6, 0xB8);

    private int _hoverIndex = -1;
    private List<Point> _plotPoints = [];

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(List<FundNavChartPoint>), typeof(FundNavChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDataChanged));

    public static readonly DependencyProperty MarkersProperty =
        DependencyProperty.Register(nameof(Markers), typeof(List<FundTradeMarker>), typeof(FundNavChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public List<FundNavChartPoint>? Points
    {
        get => (List<FundNavChartPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public List<FundTradeMarker>? Markers
    {
        get => (List<FundTradeMarker>?)GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    static FundNavChartControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FundNavChartControl),
            new FrameworkPropertyMetadata(typeof(FundNavChartControl)));
    }

    public FundNavChartControl()
    {
        Height = 220;
        Background = Brushes.Transparent;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) =>
        {
            _hoverIndex = -1;
            InvalidateVisual();
            UpdateHoverTexts(-1);
        };
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FundNavChartControl c)
        {
            c._hoverIndex = -1;
            c.InvalidateVisual();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Points == null || Points.Count == 0 || _plotPoints.Count == 0) return;
        var pos = e.GetPosition(this);
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < _plotPoints.Count; i++)
        {
            double d = Math.Abs(_plotPoints[i].X - pos.X);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        if (best != _hoverIndex)
        {
            _hoverIndex = best;
            InvalidateVisual();
            UpdateHoverTexts(best);
        }
    }

    private void UpdateHoverTexts(int index)
    {
        if (DataContext is not StockItem stock) return;
        if (Points == null || index < 0 || index >= Points.Count)
        {
            stock.FundNavHoverDate = Points is { Count: > 0 }
                ? Points[^1].Date.ToString("yyyy-MM-dd")
                : "";
            stock.FundNavHoverReturn = Points is { Count: > 0 }
                ? $"{Points[^1].ReturnPercent:+0.00;-0.00;0.00}%"
                : "";
            return;
        }

        stock.FundNavHoverDate = Points[index].Date.ToString("yyyy-MM-dd");
        stock.FundNavHoverReturn = $"{Points[index].ReturnPercent:+0.00;-0.00;0.00}%";
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _plotPoints = [];
        var points = Points;
        if (points == null || points.Count == 0)
        {
            DrawCenteredText(dc, "暂无净值数据", ActualWidth / 2, ActualHeight / 2, ColorAxis, 12);
            return;
        }

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 40 || h < 40) return;

        const double padL = 48, padR = 12, padT = 18, padB = 28;
        double plotW = w - padL - padR;
        double plotH = h - padT - padB;
        if (plotW <= 0 || plotH <= 0) return;

        decimal minR = points.Min(p => p.ReturnPercent);
        decimal maxR = points.Max(p => p.ReturnPercent);
        if (maxR == minR)
        {
            maxR += 1;
            minR -= 1;
        }
        double range = (double)(maxR - minR);

        // grid
        var gridPen = new Pen(new SolidColorBrush(ColorGrid), 1) { DashStyle = DashStyles.Dash };
        for (int i = 0; i <= 4; i++)
        {
            double y = padT + plotH * i / 4;
            dc.DrawLine(gridPen, new Point(padL, y), new Point(padL + plotW, y));
            decimal val = maxR - (maxR - minR) * i / 4;
            DrawText(dc, $"{val:0.00}%", 2, y - 7, ColorAxis, 10);
        }

        // line path
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < points.Count; i++)
            {
                double x = padL + (points.Count == 1 ? plotW / 2 : plotW * i / (points.Count - 1));
                double y = padT + plotH * (double)(maxR - points[i].ReturnPercent) / range;
                _plotPoints.Add(new Point(x, y));
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(ColorRed), 1.8), geo);

        // high / low labels
        int hi = 0, lo = 0;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].ReturnPercent > points[hi].ReturnPercent) hi = i;
            if (points[i].ReturnPercent < points[lo].ReturnPercent) lo = i;
        }
        DrawTag(dc, $"{points[hi].ReturnPercent:0.00}%", _plotPoints[hi], above: true);
        DrawTag(dc, $"{points[lo].ReturnPercent:0.00}%", _plotPoints[lo], above: false);

        // trade markers
        if (Markers != null)
        {
            foreach (var m in Markers)
            {
                int idx = FindNearestIndex(points, m.Date);
                if (idx < 0) continue;
                var pt = _plotPoints[idx];
                bool buy = m.Side == TradeSide.Buy;
                DrawTradeLabel(dc, buy ? "买入" : "卖出", pt, buy);
            }
        }

        // x labels
        DrawText(dc, points[0].Date.ToString("M-d"), padL, h - 20, ColorAxis, 10);
        DrawText(dc, points[^1].Date.ToString("M-d"), padL + plotW - 28, h - 20, ColorAxis, 10);
        if (points.Count > 2)
        {
            int mid = points.Count / 2;
            DrawText(dc, points[mid].Date.ToString("M-d"), _plotPoints[mid].X - 14, h - 20, ColorAxis, 10);
        }

        // hover
        if (_hoverIndex >= 0 && _hoverIndex < _plotPoints.Count)
        {
            var hp = _plotPoints[_hoverIndex];
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x99, 0x99, 0x99)), 1),
                new Point(hp.X, padT), new Point(hp.X, padT + plotH));
            dc.DrawEllipse(new SolidColorBrush(ColorRed), null, hp, 3.5, 3.5);
        }
    }

    private static int FindNearestIndex(List<FundNavChartPoint> points, DateTime date)
    {
        int best = -1;
        double bestDays = double.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            double d = Math.Abs((points[i].Date.Date - date.Date).TotalDays);
            if (d < bestDays)
            {
                bestDays = d;
                best = i;
            }
        }
        return bestDays <= 3 ? best : -1;
    }

    private static void DrawTradeLabel(DrawingContext dc, string text, Point anchor, bool buy)
    {
        var bg = new SolidColorBrush(buy ? ColorRed : ColorGreen);
                    var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            LabelTypeface, 11, Brushes.White, 1.0);
        double bw = ft.Width + 10;
        double bh = ft.Height + 4;
        double x = anchor.X - bw / 2;
        double y = buy ? anchor.Y - bh - 14 : anchor.Y + 10;
        dc.DrawRoundedRectangle(bg, null, new Rect(x, y, bw, bh), 3, 3);
        dc.DrawText(ft, new Point(x + 5, y + 2));
        dc.DrawLine(new Pen(bg, 1), new Point(anchor.X, buy ? y + bh : y), anchor);
    }

    private static void DrawTag(DrawingContext dc, string text, Point anchor, bool above)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            LabelTypeface, 10, new SolidColorBrush(ColorAxis), 1.0);
        double y = above ? anchor.Y - 16 : anchor.Y + 4;
        dc.DrawText(ft, new Point(anchor.X + 4, y));
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, Color color, double size)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            LabelTypeface, size, new SolidColorBrush(color), 1.0);
        dc.DrawText(ft, new Point(x, y));
    }

    private static void DrawCenteredText(DrawingContext dc, string text, double x, double y, Color color, double size)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            LabelTypeface, size, new SolidColorBrush(color), 1.0);
        dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
    }
}
