using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StockTool.Services;

public class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private readonly int _hotkeyId = 9001;
    private HwndSource? _hwndSource;
    private uint _modifiers;
    private uint _key;
    private bool _registered;

    public event Action? HotkeyPressed;

    public HotkeyService(Window window)
    {
        _window = window;
    }

    public bool Register(string hotkey)
    {
        Unregister();

        if (!ParseHotkey(hotkey, out _modifiers, out _key))
            return false;

        var handle = new WindowInteropHelper(_window).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource!.AddHook(WndProc);

        _registered = RegisterHotKey(handle, _hotkeyId, _modifiers, _key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _hwndSource != null)
        {
            var handle = new WindowInteropHelper(_window).Handle;
            if (handle != IntPtr.Zero)
                UnregisterHotKey(handle, _hotkeyId);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
            _registered = false;
        }
    }

    public static bool ParseHotkey(string hotkey, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;

        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        var parts = hotkey.Split('+');
        foreach (var part in parts)
        {
            switch (part.Trim().ToLower())
            {
                case "ctrl":  modifiers |= MOD_CONTROL; break;
                case "alt":   modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win":   modifiers |= MOD_WIN; break;
                default:
                    if (part.Length == 1)
                        key = (uint)char.ToUpper(part[0]);
                    else if (part.StartsWith("f", StringComparison.OrdinalIgnoreCase) &&
                             int.TryParse(part[1..], out int fNum) && fNum >= 1 && fNum <= 24)
                        key = (uint)(0x70 + fNum - 1); // VK_F1 = 0x70
                    else
                        return false;
                    break;
            }
        }

        return modifiers > 0 && key > 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
    }
}
