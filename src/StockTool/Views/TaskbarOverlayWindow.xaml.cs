using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using StockTool.Core.Models;
using StockTool.Services;
using StockTool.ViewModels;

namespace StockTool.Views;

public partial class TaskbarOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int SwpNosize = 0x0001;
    private const int SwpNomove = 0x0002;
    private const int SwpNoactivate = 0x0010;
    private const int SwpShowwindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    public int ScreenIndex { get; }

    /// <summary>双击某只任务栏股票时触发。</summary>
    public event Action<StockItem>? StockDoubleClicked;

    public static readonly DependencyProperty PrimaryTextBrushProperty =
        DependencyProperty.Register(nameof(PrimaryTextBrush), typeof(Brush), typeof(TaskbarOverlayWindow),
            new PropertyMetadata(Brushes.Black));

    public static readonly DependencyProperty SecondaryTextBrushProperty =
        DependencyProperty.Register(nameof(SecondaryTextBrush), typeof(Brush), typeof(TaskbarOverlayWindow),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))));

    public static readonly DependencyProperty SparklineWidthProperty =
        DependencyProperty.Register(nameof(SparklineWidth), typeof(double), typeof(TaskbarOverlayWindow),
            new FrameworkPropertyMetadata(55.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
                OnSparklineWidthChanged));

    public static readonly DependencyProperty ClockGapProperty =
        DependencyProperty.Register(nameof(ClockGap), typeof(double), typeof(TaskbarOverlayWindow),
            new PropertyMetadata(4.0));

    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(TaskbarOverlayWindow),
            new PropertyMetadata(0.0));

    public Brush PrimaryTextBrush
    {
        get => (Brush)GetValue(PrimaryTextBrushProperty);
        set => SetValue(PrimaryTextBrushProperty, value);
    }

    public Brush SecondaryTextBrush
    {
        get => (Brush)GetValue(SecondaryTextBrushProperty);
        set => SetValue(SecondaryTextBrushProperty, value);
    }

    public double SparklineWidth
    {
        get => (double)GetValue(SparklineWidthProperty);
        set => SetValue(SparklineWidthProperty, value);
    }

    /// <summary>与系统时钟之间的间距（DIP）。</summary>
    public double ClockGap
    {
        get => (double)GetValue(ClockGapProperty);
        set => SetValue(ClockGapProperty, value);
    }

    /// <summary>上下偏移（DIP），正数下移。</summary>
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    private static void OnSparklineWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TaskbarOverlayWindow win && win.IsLoaded)
        {
            win.UpdateLayout();
            win.PlaceOnTaskbar();
        }
    }

    public TaskbarOverlayWindow(int screenIndex)
    {
        ScreenIndex = screenIndex;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ApplyNoActivateStyle();
            TaskbarDiagLog.Info($"win[{ScreenIndex}] Loaded hwnd={HwndHex()} {Snapshot()}");
        };
        IsVisibleChanged += (_, e) =>
            TaskbarDiagLog.Info($"win[{ScreenIndex}] IsVisible {e.NewValue} {Snapshot()}");
        Closed += (_, _) =>
            TaskbarDiagLog.Trace($"win[{ScreenIndex}] Closed {Snapshot()}");
        TaskbarDiagLog.Info($"win[{ScreenIndex}] ctor");
    }

    public void ApplyTextBrush(Brush brush)
    {
        PrimaryTextBrush = brush;
        if (brush is SolidColorBrush solid)
        {
            var c = solid.Color;
            SecondaryTextBrush = new SolidColorBrush(Color.FromArgb(0xAA, c.R, c.G, c.B));
        }
        else
        {
            SecondaryTextBrush = brush;
        }
    }

    public void SetItems(IEnumerable items)
    {
        int count = 0;
        if (items is System.Collections.ICollection col)
            count = col.Count;
        else
        {
            foreach (var _ in items) count++;
        }

        TickerList.ItemsSource = items;
        UpdateLayout();
        SizeToContent = SizeToContent.WidthAndHeight;
        TaskbarDiagLog.Info($"win[{ScreenIndex}] SetItems count={count} desired={TickerList.DesiredSize.Width:0.#}x{TickerList.DesiredSize.Height:0.#}");
    }

    public void PlaceOnTaskbar()
    {
        if (!TaskbarGeometry.TryGetBand(ScreenIndex, out var band))
        {
            TaskbarDiagLog.Trace($"win[{ScreenIndex}] PlaceOnTaskbar TryGetBand FAIL → Hide {Snapshot()}");
            Hide();
            return;
        }

        Show();
        ApplyNoActivateStyle();
        // 先按内容测宽，避免 ActualWidth 偏大把叠层推得离时钟过远
        TickerList.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        TickerList.Arrange(new Rect(TickerList.DesiredSize));
        UpdateLayout();

        var hwnd = new WindowInteropHelper(this).Handle;
        TaskbarGeometry.GetWindowDpi(hwnd, out double dpiX, out double dpiY);
        var dipBand = TaskbarGeometry.ToDipRect(band.PixelRect, dpiX, dpiY);

        double w = Math.Max(Math.Max(ActualWidth, TickerList.DesiredSize.Width), 1);
        // 不压矮内容高度，避免分时图/文字下方被裁切
        double h = Math.Max(Math.Max(ActualHeight, TickerList.DesiredSize.Height), 36);

        // 固定尺寸前关掉 SizeToContent，避免宽高被旧值裁切
        SizeToContent = SizeToContent.Manual;

        // 紧贴系统时钟左侧，间距由设置页滑块控制
        double safeRightPx = TaskbarGeometry.GetSafeRightEdgePx(band.Screen, band.Edge);
        double safeRightDip = safeRightPx * 96.0 / dpiX;
        double gap = Math.Max(0, ClockGap);

        double left;
        double top;
        switch (band.Edge)
        {
            case TaskbarGeometry.DockEdge.Top:
                left = Math.Max(dipBand.Left + 8, safeRightDip - w - gap);
                // 顶栏：顶边对齐，过高时向下溢出
                top = dipBand.Top;
                break;
            case TaskbarGeometry.DockEdge.Left:
                left = dipBand.Left + Math.Max(0, (dipBand.Width - w) / 2);
                top = dipBand.Bottom - h - gap;
                break;
            case TaskbarGeometry.DockEdge.Right:
                left = dipBand.Left + Math.Max(0, (dipBand.Width - w) / 2);
                top = dipBand.Bottom - h - gap;
                break;
            default:
                left = Math.Max(dipBand.Left + 8, safeRightDip - w - gap);
                // 底栏：底边对齐任务栏底，过高时向上伸出，保证下方显示全
                top = dipBand.Bottom - h;
                if (top + h > dipBand.Bottom)
                    top = dipBand.Bottom - h;
                break;
        }

        // 上下微调：由设置页滑块控制，正数下移
        top += VerticalOffset;

        Left = left;
        Top = top;
        Width = w;
        Height = h;

        ApplyTopmostNoActivate();
        TaskbarDiagLog.Info(
            $"win[{ScreenIndex}] PlaceOnTaskbar edge={band.Edge} safeRightDip={safeRightDip:0.#} " +
            $"pos=({left:0.#},{top:0.#}) size={w:0.#}x{h:0.#} itemCount={TickerList.Items.Count} {Snapshot()}");
    }

    /// <summary>
    /// 主窗 Activate/Topmost 后叠层容易被压到任务栏下，需重新 HWND_TOPMOST。
    /// </summary>
    public void EnsureTopmost()
    {
        if (!IsVisible || ActualWidth < 1 || ActualHeight < 1)
        {
            TaskbarDiagLog.Warn($"win[{ScreenIndex}] EnsureTopmost → PlaceOnTaskbar (bad state) {Snapshot()}");
            PlaceOnTaskbar();
            return;
        }

        ApplyTopmostNoActivate();
    }

    private void ApplyTopmostNoActivate()
    {
        ApplyNoActivateStyle();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            TaskbarDiagLog.Warn($"win[{ScreenIndex}] ApplyTopmostNoActivate hwnd=0");
            return;
        }

        bool ok = SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
        if (!ok)
            TaskbarDiagLog.Warn($"win[{ScreenIndex}] SetWindowPos failed err={Marshal.GetLastWin32Error()} hwnd={HwndHex()}");
    }

    private string HwndHex()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        return hwnd == IntPtr.Zero ? "0" : $"0x{hwnd.ToInt64():X}";
    }

    private string Snapshot()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool winVisible = hwnd != IntPtr.Zero && IsWindowVisible(hwnd);
        int ex = hwnd != IntPtr.Zero ? GetWindowLong(hwnd, GwlExStyle) : 0;
        return $"vis={IsVisible} winVis={winVisible} hwnd={HwndHex()} " +
               $"bounds=({Left:0.#},{Top:0.#},{ActualWidth:0.#}x{ActualHeight:0.#}) " +
               $"sizeToContent={SizeToContent} topmost={Topmost} ex=0x{ex:X} items={TickerList.Items.Count}";
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyNoActivateStyle();

    private void ApplyNoActivateStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // 仅在缺少标志时改 exstyle；反复 SetWindowLong 会打乱 Z 序导致“点一下就不见”
        int ex = GetWindowLong(hwnd, GwlExStyle);
        int next = ex | WsExToolWindow | WsExNoActivate;
        if (next != ex)
            SetWindowLong(hwnd, GwlExStyle, next);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyNoActivateStyle();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 任意点击先吞掉并置顶，避免点穿/任务栏抢 Z 序
        e.Handled = true;
        ApplyTopmostNoActivate();
        TaskbarDiagLog.Info($"win[{ScreenIndex}] PreviewMouse click={e.ClickCount} src={e.OriginalSource?.GetType().Name} {Snapshot()}");

        Dispatcher.BeginInvoke(ApplyTopmostNoActivate, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        if (e.ClickCount < 2) return;

        var fe = e.OriginalSource as DependencyObject;
        while (fe != null && fe is not TaskbarOverlayWindow)
        {
            if (fe is FrameworkElement { DataContext: TaskbarStockViewItem item })
            {
                TaskbarDiagLog.Info($"win[{ScreenIndex}] DoubleClick → open code={item.Stock.Code} name={item.Stock.DisplayName}");
                try
                {
                    StockDoubleClicked?.Invoke(item.Stock);
                }
                catch (Exception ex)
                {
                    TaskbarDiagLog.Error($"win[{ScreenIndex}] DoubleClick invoke threw", ex);
                    throw;
                }
                return;
            }
            fe = System.Windows.Media.VisualTreeHelper.GetParent(fe);
        }
    }

    // 保留 Border 上的 handler 作兜底（Preview 已 Handled 时通常不会再到这里）
    private void Ticker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ApplyTopmostNoActivate();
        TaskbarDiagLog.Info($"win[{ScreenIndex}] TickerMouseDown click={e.ClickCount} {Snapshot()}");
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}
