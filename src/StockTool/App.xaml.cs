using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Shell;
using StockTool.Core.Services;
using StockTool.Data;
using StockTool.Services;
using StockTool.ViewModels;
using WinForms = System.Windows.Forms;
using WinDrawing = System.Drawing;

namespace StockTool;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\StockTool.SingleInstance";
    private const string ExitEventName = "Local\\StockTool.Exit";
    private const string ShowEventName = "Local\\StockTool.Show";
    private const string QuitArg = "--quit";

    private WinForms.NotifyIcon? _trayIcon;
    private EastMoneyClient? _eastMoneyClient;
    private HotkeyService? _hotkeyService;
    private MainWindow? _mainWindow;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _exitEvent;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _exitWatchCts;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => string.Equals(a, QuitArg, StringComparison.OrdinalIgnoreCase)))
        {
            RequestRunningInstanceExit();
            Shutdown();
            return;
        }

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            RequestRunningInstanceShow();
            Shutdown();
            return;
        }

        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _exitWatchCts = new CancellationTokenSource();
        _ = WatchIpcSignalsAsync(_exitWatchCts.Token);

        var configStore = new ConfigStore();
        _eastMoneyClient = new EastMoneyClient();

        var viewModel = new MainViewModel(configStore, _eastMoneyClient);
        viewModel.AlertTriggered += message => _mainWindow?.Dispatcher.Invoke(() =>
        {
            _trayIcon?.ShowBalloonTip(4000, "盯盘预警", message, WinForms.ToolTipIcon.Info);
        });

        _mainWindow = new MainWindow(viewModel, _eastMoneyClient);

        // global hotkey
        _hotkeyService = new HotkeyService(_mainWindow);
        _hotkeyService.HotkeyPressed += () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.ToggleVisibility());
        TryRegisterHotkey(viewModel.Hotkey);

        // tray icon with custom icon
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
        var trayIcon = File.Exists(iconPath)
            ? new WinDrawing.Icon(iconPath)
            : WinDrawing.SystemIcons.Application;

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = trayIcon,
            Text = "盯盘",
            Visible = true
        };

        var powerImage = CreatePowerGlyphImage(16);
        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("显示", null, (s, args) => ShowMainWindow());
        contextMenu.Items.Add("设置", null, (s, args) => _mainWindow.Dispatcher.Invoke(() => _mainWindow.OpenSettings()));
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(new WinForms.ToolStripMenuItem("退出程序", powerImage, (s, args) => ShutdownApp()));
        _trayIcon.ContextMenuStrip = contextMenu;

        _trayIcon.DoubleClick += (s, args) => ShowMainWindow();

        _mainWindow.SettingsChanged += OnSettingsChanged;
        SetupTaskbarJumpList();
        _mainWindow.Show();
    }

    // 任务栏右键最下方：退出程序（开关机图标）
    private static void SetupTaskbarJumpList()
    {
        string? appPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(appPath)) return;

        string powerIconPath = EnsurePowerIconFile();

        var quitTask = new JumpTask
        {
            Title = "退出程序",
            Description = "完全退出盯盘后台",
            ApplicationPath = appPath,
            Arguments = QuitArg,
            IconResourcePath = powerIconPath,
            IconResourceIndex = 0,
            // 独立分类，任务栏列表中靠下显示
            CustomCategory = " "
        };

        var jumpList = new JumpList();
        jumpList.ShowFrequentCategory = false;
        jumpList.ShowRecentCategory = false;
        jumpList.JumpItems.Add(quitTask);
        JumpList.SetJumpList(Current, jumpList);
    }

    private static string EnsurePowerIconFile()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockTool");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "power.ico");
        SavePowerIcon(path);
        return path;
    }

    private static void SavePowerIcon(string path)
    {
        using var bmp = CreatePowerGlyphImage(32);
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var icon = WinDrawing.Icon.FromHandle(hIcon);
            using var fs = File.Create(path);
            icon.Save(fs);
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    // 绘制开关机符号
    private static WinDrawing.Bitmap CreatePowerGlyphImage(int size)
    {
        var bmp = new WinDrawing.Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = WinDrawing.Graphics.FromImage(bmp);
        g.Clear(WinDrawing.Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float s = size;
        using var pen = new WinDrawing.Pen(WinDrawing.Color.FromArgb(255, 232, 232, 232), Math.Max(1.6f, s * 0.08f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        float cx = s / 2f;
        float stemTop = s * 0.16f;
        float stemBottom = s * 0.48f;
        g.DrawLine(pen, cx, stemTop, cx, stemBottom);

        float pad = s * 0.18f;
        g.DrawArc(pen, pad, pad + s * 0.06f, s - pad * 2, s - pad * 2, 125f, 290f);
        return bmp;
    }

    private static void RequestRunningInstanceExit()
    {
        try
        {
            using var exitEvent = EventWaitHandle.OpenExisting(ExitEventName);
            exitEvent.Set();
            return;
        }
        catch
        {
            // 无运行中实例时，兜底结束同名进程
        }

        try
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id == current.Id) continue;
                try { p.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch
        {
        }
    }

    private static void RequestRunningInstanceShow()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch
        {
            // 运行中实例刚退出时忽略
        }
    }

    private async Task WatchIpcSignalsAsync(CancellationToken token)
    {
        await Task.Yield();
        while (!token.IsCancellationRequested)
        {
            int signaled = await Task.Run(() =>
            {
                try
                {
                    if (_exitEvent == null || _showEvent == null)
                        return WaitHandle.WaitTimeout;
                    return WaitHandle.WaitAny([_exitEvent, _showEvent], 500);
                }
                catch
                {
                    return WaitHandle.WaitTimeout;
                }
            }, token).ConfigureAwait(false);

            if (token.IsCancellationRequested) break;
            if (signaled == WaitHandle.WaitTimeout) continue;

            if (signaled == 0)
            {
                await Dispatcher.InvokeAsync(ShutdownApp);
                break;
            }

            if (signaled == 1)
                await Dispatcher.InvokeAsync(ShowMainWindow);
        }
    }

    private void TryRegisterHotkey(string hotkey)
    {
        _hotkeyService?.Unregister();
        bool ok = _hotkeyService?.Register(hotkey) ?? false;
        if (!ok && !string.IsNullOrWhiteSpace(hotkey))
        {
            System.Windows.MessageBox.Show(
                $"热键 {hotkey} 注册失败，可能已被其他程序占用。\n请在设置中更换热键。",
                "热键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSettingsChanged(string newHotkey)
    {
        TryRegisterHotkey(newHotkey);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;

        bool wasTopmost = _mainWindow.Topmost;
        _mainWindow.Topmost = true;
        _mainWindow.Activate();
        _mainWindow.Topmost = wasTopmost;
        _mainWindow.Focus();
    }

    private void ShutdownApp()
    {
        _exitWatchCts?.Cancel();
        _hotkeyService?.Dispose();
        if (_mainWindow?.DataContext is ViewModels.MainViewModel vm)
            vm.DisposeTaskbarOverlay();
        _eastMoneyClient?.Dispose();
        _trayIcon?.Dispose();
        _mainWindow?.CloseForExit();
        _exitEvent?.Dispose();
        _showEvent?.Dispose();
        _singleInstanceMutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitWatchCts?.Cancel();
        _hotkeyService?.Dispose();
        if (_mainWindow?.DataContext is ViewModels.MainViewModel vm)
            vm.DisposeTaskbarOverlay();
        _eastMoneyClient?.Dispose();
        _trayIcon?.Dispose();
        _exitEvent?.Dispose();
        _showEvent?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
