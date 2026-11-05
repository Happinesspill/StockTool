using System.Runtime.InteropServices;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace StockTool.Services;

/// <summary>多显示器任务栏几何：像素坐标与排序。</summary>
public static class TaskbarGeometry
{
    public readonly record struct TaskbarBand(WinForms.Screen Screen, int ScreenIndex, Rect PixelRect, DockEdge Edge);

    public enum DockEdge { Bottom, Top, Left, Right }

    /// <summary>主屏优先，其余按 X 坐标排序；ScreenIndex 从 1 起。</summary>
    public static WinForms.Screen[] GetOrderedScreens()
    {
        return WinForms.Screen.AllScreens
            .OrderByDescending(s => s.Primary)
            .ThenBy(s => s.Bounds.X)
            .ThenBy(s => s.Bounds.Y)
            .ToArray();
    }

    public static int GetScreenCount() => Math.Max(1, WinForms.Screen.AllScreens.Length);

    public static bool TryGetBand(int screenIndex1Based, out TaskbarBand band)
    {
        band = default;
        var screens = GetOrderedScreens();
        if (screenIndex1Based < 1 || screenIndex1Based > screens.Length)
            return false;

        var screen = screens[screenIndex1Based - 1];
        if (!TryGetTaskbarRect(screen, out var rect, out var edge))
            return false;

        band = new TaskbarBand(screen, screenIndex1Based, rect, edge);
        return true;
    }

    public static bool TryGetTaskbarRect(WinForms.Screen screen, out Rect rect, out DockEdge edge)
    {
        var b = screen.Bounds;
        var w = screen.WorkingArea;

        // 底部任务栏
        if (w.Bottom < b.Bottom)
        {
            rect = new Rect(b.Left, w.Bottom, b.Width, b.Bottom - w.Bottom);
            edge = DockEdge.Bottom;
            return rect.Height > 0;
        }

        // 顶部
        if (w.Top > b.Top)
        {
            rect = new Rect(b.Left, b.Top, b.Width, w.Top - b.Top);
            edge = DockEdge.Top;
            return rect.Height > 0;
        }

        // 左侧
        if (w.Left > b.Left)
        {
            rect = new Rect(b.Left, b.Top, w.Left - b.Left, b.Height);
            edge = DockEdge.Left;
            return rect.Width > 0;
        }

        // 右侧
        if (w.Right < b.Right)
        {
            rect = new Rect(w.Right, b.Top, b.Right - w.Right, b.Height);
            edge = DockEdge.Right;
            return rect.Width > 0;
        }

        // 该屏无独立任务栏时，贴底模拟一条区域（扩展屏常见）
        const int fallbackH = 48;
        rect = new Rect(b.Left, b.Bottom - fallbackH, b.Width, fallbackH);
        edge = DockEdge.Bottom;
        return true;
    }

    /// <summary>将屏幕像素矩形转换为 WPF DIP（按目标窗体 DPI）。</summary>
    public static Rect ToDipRect(Rect pixelRect, double dpiX, double dpiY)
    {
        if (dpiX <= 0) dpiX = 96;
        if (dpiY <= 0) dpiY = 96;
        return new Rect(
            pixelRect.X * 96.0 / dpiX,
            pixelRect.Y * 96.0 / dpiY,
            pixelRect.Width * 96.0 / dpiX,
            pixelRect.Height * 96.0 / dpiY);
    }

    /// <summary>
    /// 取得系统时钟左缘像素 X，叠层紧贴其左侧（不按整块通知区，避免离时间过远）。
    /// </summary>
    public static double GetSafeRightEdgePx(WinForms.Screen screen, DockEdge edge)
    {
        if (TryGetClockLeft(screen, out int clockLeft))
            return clockLeft;

        // 兜底：只避开时钟+显示桌面按钮的大致宽度
        const int reserve = 96;
        return edge switch
        {
            DockEdge.Left => screen.Bounds.Left + reserve,
            DockEdge.Right => screen.Bounds.Right - reserve,
            _ => screen.Bounds.Right - reserve
        };
    }

    private static bool TryGetClockLeft(WinForms.Screen screen, out int left)
    {
        left = 0;
        if (TryClockLeftOnTray(FindWindow("Shell_TrayWnd", null), screen, out left))
            return true;

        IntPtr secondary = IntPtr.Zero;
        while (true)
        {
            secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null);
            if (secondary == IntPtr.Zero)
                break;
            if (TryClockLeftOnTray(secondary, screen, out left))
                return true;
        }

        return false;
    }

    private static bool TryClockLeftOnTray(IntPtr tray, WinForms.Screen screen, out int left)
    {
        left = 0;
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out var trayRect))
            return false;

        var trayBounds = new System.Drawing.Rectangle(
            trayRect.Left, trayRect.Top,
            trayRect.Right - trayRect.Left,
            trayRect.Bottom - trayRect.Top);
        if (!screen.Bounds.IntersectsWith(trayBounds))
            return false;

        // 优先精确定位时钟控件（Win10 TrayClockWClass；Win11 可能仍可用）
        if (TryFindClockInTree(tray, screen, out left))
            return true;

        IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && GetWindowRect(notify, out var nr) && nr.Right > nr.Left)
        {
            // 通知区右端才是时钟；按约 88px 时钟宽回退，紧贴时间
            const int clockWidth = 88;
            left = Math.Max(nr.Left, nr.Right - clockWidth);
            return true;
        }

        left = trayRect.Right - 88;
        return true;
    }

    private static bool TryFindClockInTree(IntPtr root, WinForms.Screen screen, out int left)
    {
        left = 0;
        IntPtr best = IntPtr.Zero;
        int bestLeft = int.MinValue;

        void Walk(IntPtr parent)
        {
            IntPtr child = IntPtr.Zero;
            while (true)
            {
                child = FindWindowEx(parent, child, null, null);
                if (child == IntPtr.Zero)
                    break;

                var cls = new System.Text.StringBuilder(256);
                GetClassName(child, cls, cls.Capacity);
                string name = cls.ToString();
                bool isClock = name.Contains("TrayClock", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("ClockFlyout", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "ClockButton", StringComparison.OrdinalIgnoreCase);

                if (isClock && GetWindowRect(child, out var r) && r.Right > r.Left)
                {
                    var bounds = new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                    if (screen.Bounds.IntersectsWith(bounds) && r.Left > bestLeft)
                    {
                        bestLeft = r.Left;
                        best = child;
                    }
                }

                Walk(child);
            }
        }

        Walk(root);
        if (best == IntPtr.Zero)
            return false;

        left = bestLeft;
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public static void GetWindowDpi(IntPtr hwnd, out double dpiX, out double dpiY)
    {
        if (hwnd != IntPtr.Zero)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0)
                {
                    dpiX = dpi;
                    dpiY = dpi;
                    return;
                }
            }
            catch
            {
                // 旧系统无 GetDpiForWindow
            }
        }

        dpiX = 96;
        dpiY = 96;
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            dpiX = g.DpiX;
            dpiY = g.DpiY;
        }
        catch
        {
            // keep 96
        }
    }
}
