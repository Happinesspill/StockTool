using System.IO;
using System.Reflection;
using System.Windows;
using StockTool.Core.Services;
using StockTool.Data;
using StockTool.Services;
using StockTool.ViewModels;
using WinForms = System.Windows.Forms;
using WinDrawing = System.Drawing;

namespace StockTool;

public partial class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _trayIcon;
    private EastMoneyClient? _eastMoneyClient;
    private HotkeyService? _hotkeyService;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("显示", null, (s, args) => ShowMainWindow());
        contextMenu.Items.Add("设置", null, (s, args) => _mainWindow.Dispatcher.Invoke(() => _mainWindow.OpenSettings()));
        contextMenu.Items.Add("退出", null, (s, args) => ShutdownApp());
        _trayIcon.ContextMenuStrip = contextMenu;

        _trayIcon.DoubleClick += (s, args) => ShowMainWindow();

        _mainWindow.SettingsChanged += OnSettingsChanged;
        _mainWindow.Show();
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
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    private void ShutdownApp()
    {
        _hotkeyService?.Dispose();
        _eastMoneyClient?.Dispose();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _eastMoneyClient?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
