using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using StockTool.Services;

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

    public static readonly DependencyProperty PrimaryTextBrushProperty =
        DependencyProperty.Register(nameof(PrimaryTextBrush), typeof(Brush), typeof(TaskbarOverlayWindow),
            new PropertyMetadata(Brushes.Black));

    public static readonly DependencyProperty SecondaryTextBrushProperty =
        DependencyProperty.Register(nameof(SecondaryTextBrush), typeof(Brush), typeof(TaskbarOverlayWindow),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))));

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

    public TaskbarOverlayWindow(int screenIndex)
    {
        ScreenIndex = screenIndex;
        InitializeComponent();
        ApplyThemeColors();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => ApplyNoActivateStyle();
    }

    private void ApplyThemeColors()
    {
        bool light = IsSystemLightTheme();
        PrimaryTextBrush = light
            ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
            : Brushes.White;
        SecondaryTextBrush = light
            ? new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))
            : new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("SystemUsesLightTheme");
            if (value is int i) return i != 0;
        }
        catch
        {
            // ignore
        }
        return true;
    }

    public void SetItems(IEnumerable items)
    {
        TickerList.ItemsSource = items;
        UpdateLayout();
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    public void PlaceOnTaskbar()
    {
        if (!TaskbarGeometry.TryGetBand(ScreenIndex, out var band))
        {
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
        double h = Math.Max(Math.Max(ActualHeight, TickerList.DesiredSize.Height), 28);

        // 紧贴系统时钟左侧
        double safeRightPx = TaskbarGeometry.GetSafeRightEdgePx(band.Screen, band.Edge);
        double safeRightDip = safeRightPx * 96.0 / dpiX;
        const double gap = 4;

        double left;
        double top;
        switch (band.Edge)
        {
            case TaskbarGeometry.DockEdge.Top:
                left = Math.Max(dipBand.Left + 8, safeRightDip - w - gap);
                top = dipBand.Top + Math.Max(0, (dipBand.Height - h) / 2);
                break;
            case TaskbarGeometry.DockEdge.Left:
                left = dipBand.Left + Math.Max(0, (dipBand.Width - w) / 2);
                top = Math.Max(dipBand.Top + 8, dipBand.Bottom - h - gap);
                break;
            case TaskbarGeometry.DockEdge.Right:
                left = dipBand.Left + Math.Max(0, (dipBand.Width - w) / 2);
                top = Math.Max(dipBand.Top + 8, dipBand.Bottom - h - gap);
                break;
            default:
                left = Math.Max(dipBand.Left + 8, safeRightDip - w - gap);
                top = dipBand.Top + Math.Max(0, (dipBand.Height - h) / 2);
                break;
        }

        Left = left;
        Top = top;
        Width = w;
        Height = h;

        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
                SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyNoActivateStyle();

    private void ApplyNoActivateStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int ex = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, ex | WsExToolWindow | WsExNoActivate);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyNoActivateStyle();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
