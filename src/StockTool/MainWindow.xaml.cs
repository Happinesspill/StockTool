using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using StockTool.Core.Models;
using StockTool.Data;
using StockTool.ViewModels;
using StockTool.Views;

namespace StockTool;

public partial class MainWindow : Window
{
    private bool _isReallyClosing;
    private readonly EastMoneyClient _client;
    private MainViewModel VM => (MainViewModel)DataContext;

    // drag-drop state
    private Point _dragStart;
    private StockItem? _draggedItem;
    /// <summary>遮罩点击收起后，短暂忽略列表拖拽/点击，避免穿透。</summary>
    private bool _suppressListInput;

    public event Action<string>? SettingsChanged;

    public MainWindow(MainViewModel viewModel, EastMoneyClient client)
    {
        _client = client;
        DataContext = viewModel;
        InitializeComponent();
        ApplyAppIcon();

        // restore saved position
        if (!double.IsNaN(VM.WindowLeft) && !double.IsNaN(VM.WindowTop))
        {
            Left = VM.WindowLeft;
            Top = VM.WindowTop;
        }
    }

    private void ApplyAppIcon()
    {
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
        if (!File.Exists(iconPath)) return;
        Icon = BitmapFrame.Create(new Uri(iconPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddStockDialog(_client) { Owner = this };
        if (dialog.ShowDialog() == true
            && !string.IsNullOrEmpty(dialog.SelectedInternalCode)
            && !string.IsNullOrEmpty(dialog.SelectedName))
        {
            VM.AddStock(dialog.SelectedInternalCode, dialog.SelectedName, dialog.SelectedMarket ?? "");
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    public void OpenSettings()
    {
        var dialog = new SettingsWindow(VM.Opacity, VM.FontSize, VM.RefreshInterval, VM.Hotkey, VM.Topmost, VM.ShowMarketTag)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            VM.SaveSettings(dialog.OpacityValue, dialog.FontSizeValue, dialog.RefreshInterval,
                            dialog.Hotkey, dialog.IsTopmost, dialog.ShowMarketTag);
            SettingsChanged?.Invoke(dialog.Hotkey);
        }
    }

    public void ToggleVisibility()
    {
        if (Visibility == Visibility.Visible)
            Hide();
        else
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    private void BtnHide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _isReallyClosing = true;
        Application.Current.Shutdown();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isReallyClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Window_LocationChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal && IsLoaded)
        {
            VM.SaveWindowState(Left, Top, ActualWidth, ActualHeight);
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WindowState == WindowState.Normal && IsLoaded)
        {
            VM.SaveWindowState(Left, Top, ActualWidth, ActualHeight);
        }
    }

    // ── Drag-Drop Reorder ───────────────────────────

    private void BtnSortPrice_Click(object sender, RoutedEventArgs e)
    {
        VM.TogglePriceSort();
        e.Handled = true;
    }

    private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptSimpleText("新建分组", "分组名称");
        if (name == null) return;
        if (VM.AddGroup(name) == null)
            MessageBox.Show(this, "创建失败（名称无效或已存在）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnManageGroups_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ManageGroupsWindow(VM) { Owner = this };
        dlg.ShowDialog();
    }

    private string? PromptSimpleText(string title, string placeholder)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 300,
            Height = 150,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Topmost = true
        };

        string? result = null;
        var box = new TextBox
        {
            FontSize = 14,
            Margin = new Thickness(16, 12, 16, 8),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = placeholder
        };

        var ok = new Button { Content = "确定", Width = 72, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 72, Height = 30 };
        ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) { result = box.Text; dialog.DialogResult = true; }
            if (ke.Key == Key.Escape) dialog.DialogResult = false;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        dialog.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(16, 14, 16, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
                    },
                    box,
                    buttons
                }
            }
        };
        dialog.Loaded += (_, _) => box.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }

    private void StockList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_suppressListInput || VM.SelectedStock != null || !VM.CanReorder)
        {
            _draggedItem = null;
            return;
        }

        _dragStart = e.GetPosition(null);
        _draggedItem = FindStockItemAtMouse(e);
    }

    private void StockList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_suppressListInput || VM.SelectedStock != null || !VM.CanReorder) return;
        if (_draggedItem == null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var item = _draggedItem;
            _draggedItem = null;
            DragDrop.DoDragDrop(StockList, item, DragDropEffects.Move);
        }
    }

    private void StockList_DragOver(object sender, DragEventArgs e)
    {
        if (_suppressListInput || VM.SelectedStock != null || !VM.CanReorder)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(typeof(StockItem)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void StockList_Drop(object sender, DragEventArgs e)
    {
        if (_suppressListInput || VM.SelectedStock != null || !VM.CanReorder) return;
        if (!e.Data.GetDataPresent(typeof(StockItem))) return;
        var dragged = e.Data.GetData(typeof(StockItem)) as StockItem;
        if (dragged == null) return;

        var target = FindStockItemAtMouse(e);
        if (target == null || target == dragged) return;

        int oldIndex = VM.VisibleStocks.IndexOf(dragged);
        int newIndex = VM.VisibleStocks.IndexOf(target);
        VM.MoveVisibleStock(oldIndex, newIndex);
    }

    private void StockList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        // keep default arrow cursor
        e.UseDefaultCursors = false;
        e.Handled = true;
    }

    private async void StockItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (_suppressListInput) return;
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not StockItem stock) return;

        // Only toggle if mouse hasn't moved (not a drag)
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
            return;

        // 点击已结束，清除拖拽状态，避免详情打开后误触发排序
        _draggedItem = null;

        // Toggle: click same stock closes the panel
        if (VM.SelectedStock == stock)
        {
            VM.SelectedStock = null;
            return;
        }

        VM.SelectedStock = stock;
        await LoadDetailChartAsync(stock);
        AnimateSheetSlideUp(fromOffset: 72);
    }

    private void BtnExpandDetail_Click(object sender, RoutedEventArgs e)
    {
        bool expanding = !VM.IsChartExpanded;
        VM.IsChartExpanded = expanding;
        if (expanding)
            AnimateSheetSlideUp(fromOffset: 72);
        e.Handled = true;
    }

    private void BtnCollapseDetail_Click(object sender, RoutedEventArgs e)
    {
        if (VM.IsChartExpanded)
        {
            VM.IsChartExpanded = false;
            e.Handled = true;
            return;
        }
        CloseDetailSheet();
        e.Handled = true;
    }

    private void BtnCloseDetail_Click(object sender, RoutedEventArgs e)
    {
        CloseDetailSheet();
    }

    private void AnimateSheetSlideUp(double fromOffset)
    {
        if (DetailSheetTransform == null) return;

        var anim = new DoubleAnimation
        {
            From = fromOffset,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        DetailSheetTransform.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    private void DetailMask_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 按下即吞掉事件并清拖拽，松手再收起，避免遮罩消失后点击穿透到列表
        _suppressListInput = true;
        _draggedItem = null;
        e.Handled = true;
    }

    private void DetailMask_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CloseDetailSheet();
        e.Handled = true;
        // 下一帧再恢复列表输入，确保本次 Up 不会落到股票行
        Dispatcher.BeginInvoke(() => _suppressListInput = false);
    }

    private void DetailSheet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent clicks on the sheet from closing via the mask
        e.Handled = true;
    }

    private void CloseDetailSheet()
    {
        _draggedItem = null;
        VM.IsChartExpanded = false;
        VM.SelectedStock = null;
    }

    private async Task LoadDetailChartAsync(StockItem stock)
    {
        if (stock.KlinePeriod == 0)
        {
            if (stock.IntradayPoints.Count == 0)
                await LoadIntradayAsync(stock);
            return;
        }

        if (stock.KlineData.Count == 0)
            await LoadKlineAsync(stock);
    }

    private async Task LoadIntradayAsync(StockItem stock)
    {
        try
        {
            var series = await _client.GetIntradayAsync(stock.Code);
            if (series != null && series.Prices.Count > 0)
            {
                stock.IntradayPoints = series.Prices;
                stock.IntradayAvgPoints = series.AvgPrices;
            }
        }
        catch { }
    }

    private async Task LoadKlineAsync(StockItem stock)
    {
        try
        {
            var data = await _client.GetKlineAsync(stock.Code, stock.KlinePeriod);
            if (data.Count > 0)
                stock.KlineData = data;
        }
        catch { }
    }

    private async void KlinePeriod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.Tag is not string periodStr) return;

        var stock = VM.SelectedStock;
        if (stock == null) return;

        int period = int.Parse(periodStr);
        if (stock.KlinePeriod == period) return;

        stock.KlinePeriod = period;

        if (period == 0)
        {
            await LoadIntradayAsync(stock);
            return;
        }

        // 切换周期始终重新拉取，避免沿用空缓存
        stock.KlineData = [];
        await LoadKlineAsync(stock);
    }

    private StockItem? FindStockItemAtMouse(RoutedEventArgs e)
    {
        Point pos;
        if (e is MouseEventArgs me)
            pos = me.GetPosition(StockList);
        else if (e is DragEventArgs de)
            pos = de.GetPosition(StockList);
        else
            return null;

        var element = StockList.InputHitTest(pos) as DependencyObject;
        while (element != null)
        {
            if (element is FrameworkElement fe && fe.DataContext is StockItem item)
                return item;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        if (menu.PlacementTarget is not FrameworkElement element) return;
        var stock = element.Tag as StockItem;
        if (stock == null) return;

        // find the HeaderText in the ContextMenu template
        var headerText = FindVisualChild<TextBlock>(menu, "HeaderText");
        if (headerText != null)
        {
            headerText.Text = $"{stock.DisplayName} // {stock.Code} · {stock.Market}";
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;
            var result = FindVisualChild<T>(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    private void ContextMenu_Delete(object sender, RoutedEventArgs e)
    {
        var item = GetStockItemFromSender(sender);
        if (item == null) return;
        VM.RemoveStock(item);
    }

    private void ContextMenu_MoveUp(object sender, RoutedEventArgs e)
    {
        var item = GetStockItemFromSender(sender);
        if (item == null) return;
        VM.MoveStockUp(item);
    }

    private void ContextMenu_MoveDown(object sender, RoutedEventArgs e)
    {
        var item = GetStockItemFromSender(sender);
        if (item == null) return;
        VM.MoveStockDown(item);
    }

    private void ContextMenu_Rename(object sender, RoutedEventArgs e)
    {
        var stock = GetStockItemFromSender(sender);
        if (stock == null) return;

        var newName = ShowRenameDialog(stock.DisplayName, this);
        if (newName != null)
            VM.RenameStock(stock, newName);
    }

    private static string? ShowRenameDialog(string currentName, Window owner)
    {
        var dialog = new Window
        {
            Title = "重命名",
            Width = 320,
            Height = 160,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        // rounded textbox via border wrapper
        var textBoxBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 14),
            Height = 34
        };

        var textBox = new System.Windows.Controls.TextBox
        {
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontSize = 14
        };
        textBoxBorder.Child = textBox;

        // rounded cancel button
        var cancelBtn = CreateRoundedButton("取消", false);
        cancelBtn.Width = 75;
        cancelBtn.Height = 32;

        // rounded ok button (blue)
        var okBtn = CreateRoundedButton("确定", true);
        okBtn.Width = 75;
        okBtn.Height = 32;
        okBtn.Margin = new Thickness(0, 0, 8, 0);

        string? result = null;

        okBtn.Click += (_, _) =>
        {
            result = textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(result))
                result = "";
            dialog.DialogResult = true;
        };
        cancelBtn.Click += (_, _) =>
        {
            dialog.DialogResult = false;
        };

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var label = new TextBlock
        {
            Text = "输入自定义名称（留空恢复原名）：",
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
        };

        var stack = new StackPanel { Margin = new Thickness(20) };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xD0, 0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = stack
        };

        textBox.Text = currentName;
        textBox.SelectAll();

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        stack.Children.Add(label);
        stack.Children.Add(textBoxBorder);
        stack.Children.Add(btnPanel);
        dialog.Content = border;
        dialog.MouseLeftButtonDown += (_, _) => dialog.DragMove();

        dialog.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        dialog.ShowDialog();
        return result;
    }

    private static System.Windows.Controls.Button CreateRoundedButton(string text, bool isPrimary)
    {
        var bg = isPrimary
            ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))
            : new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        var fg = isPrimary
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        var hoverBg = isPrimary
            ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
            : new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));

        var btn = new System.Windows.Controls.Button
        {
            Content = text,
            Background = bg,
            Foreground = fg,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        // rounded template
        var template = new ControlTemplate(typeof(System.Windows.Controls.Button));
        var borderElem = new FrameworkElementFactory(typeof(Border));
        borderElem.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        borderElem.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderElem.AppendChild(contentPresenter);
        template.VisualTree = borderElem;

        // hover trigger
        var hoverTrigger = new Trigger { Property = System.Windows.Controls.Button.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.Button.BackgroundProperty, hoverBg));
        template.Triggers.Add(hoverTrigger);

        btn.Template = template;
        return btn;
    }

    private static StockItem? GetStockItemFromSender(object sender)
    {
        if (sender is MenuItem menuItem
            && menuItem.Parent is ContextMenu contextMenu
            && contextMenu.PlacementTarget is FrameworkElement element)
        {
            return element.Tag as StockItem;
        }
        return null;
    }
}
