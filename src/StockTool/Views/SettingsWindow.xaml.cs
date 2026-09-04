using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StockTool.ViewModels;

namespace StockTool.Views;

public partial class SettingsWindow : Window
{
    private bool _isRecording;
    private bool _loaded;
    private readonly MainViewModel? _vm;

    public double OpacityValue { get; private set; }
    public int FontSizeValue { get; private set; }
    public int RefreshInterval { get; private set; }
    public string Hotkey { get; private set; }
    public bool IsTopmost { get; private set; }
    public bool ShowMarketTag { get; private set; }

    public SettingsWindow(double opacity, int fontSize, int refreshInterval, string hotkey, bool topmost, bool showMarketTag, MainViewModel? viewModel = null)
    {
        _vm = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        // opacity
        OpacityValue = opacity;
        SldOpacity.Value = opacity * 100;
        TxtOpacity.Text = $"{(int)(opacity * 100)}%";

        // font size
        var sizes = new[] { 12, 13, 14, 15, 16, 18, 20 };
        foreach (var s in sizes) CmbFontSize.Items.Add(s);
        CmbFontSize.SelectedItem = fontSize;
        FontSizeValue = fontSize;

        // refresh interval
        var intervals = new[] { 1, 2, 3, 4, 5 };
        foreach (var i in intervals) CmbInterval.Items.Add(i);
        CmbInterval.SelectedItem = refreshInterval;
        RefreshInterval = refreshInterval;

        // hotkey
        Hotkey = hotkey;
        TxtHotkey.Text = hotkey;

        // topmost
        ChkTopmost.IsChecked = topmost;
        IsTopmost = topmost;

        // show market tag
        ChkShowMarketTag.IsChecked = showMarketTag;
        ShowMarketTag = showMarketTag;

        _loaded = true;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded) return;
        OpacityValue = SldOpacity.Value / 100.0;
        TxtOpacity.Text = $"{(int)SldOpacity.Value}%";
    }

    private void BtnRecord_Click(object sender, RoutedEventArgs e)
    {
        _isRecording = true;
        TxtHotkey.Text = "按下组合键...";
        TxtHotkey.Focus();
    }

    private void TxtHotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecording) return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            TxtHotkey.Text = Hotkey;
            _isRecording = false;
            return;
        }

        var modifiers = "";
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers += "Ctrl+";
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers += "Alt+";
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers += "Shift+";
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers += "Win+";

        var key = e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            // just modifier key alone, ignore
            return;
        }

        string keyStr = key switch
        {
            >= Key.F1 and <= Key.F24 => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.A and <= Key.Z => key.ToString(),
            _ => ""
        };

        if (string.IsNullOrEmpty(keyStr) || string.IsNullOrEmpty(modifiers))
            return;

        Hotkey = $"{modifiers}{keyStr}";
        TxtHotkey.Text = Hotkey;
        _isRecording = false;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        OpacityValue = SldOpacity.Value / 100.0;
        FontSizeValue = (int)CmbFontSize.SelectedItem;
        RefreshInterval = (int)CmbInterval.SelectedItem;
        IsTopmost = ChkTopmost.IsChecked ?? true;
        ShowMarketTag = ChkShowMarketTag.IsChecked ?? true;
        DialogResult = true;
        Close();
    }

    private void BtnMoveIndexUp_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not FrameworkElement { Tag: string code }) return;
        _vm.MoveHomeIndex(code, -1);
        e.Handled = true;
    }

    private void BtnMoveIndexDown_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not FrameworkElement { Tag: string code }) return;
        _vm.MoveHomeIndex(code, 1);
        e.Handled = true;
    }
}
