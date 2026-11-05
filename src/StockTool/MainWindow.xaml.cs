using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
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

    // 二级页面（非模态，不继承主窗置顶）
    private AddStockDialog? _addStockDialog;
    private SettingsWindow? _settingsWindow;
    private ManageGroupsWindow? _manageGroupsWindow;

    public event Action<string>? SettingsChanged;

    public MainWindow(MainViewModel viewModel, EastMoneyClient client)
    {
        _client = client;
        DataContext = viewModel;
        InitializeComponent();
        ApplyAppIcon();
        DetailSheet.Initialize(_client);
        DetailSheet.SuppressListInputChanged += OnDetailSheetSuppressListInput;
        Loaded += (_, _) => SyncHeaderPadding();

        // restore saved position
        if (!double.IsNaN(VM.WindowLeft) && !double.IsNaN(VM.WindowTop))
        {
            Left = VM.WindowLeft;
            Top = VM.WindowTop;
        }
    }

    private void ApplyAppIcon()
    {
        var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
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
        OpenAddStockDialog();
    }

    private void OpenAddStockDialog()
    {
        if (_addStockDialog != null)
        {
            BringSecondaryToFront(_addStockDialog);
            return;
        }

        var dialog = new AddStockDialog(_client, VM.IsFundGroup, VM.GetHoldingVisionOptions, OpenSettings)
        {
            // Owner 保证压在主窗之上（主窗 Topmost 时也生效）；自身不设 Topmost
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Topmost = false
        };
        dialog.Closed += (_, _) =>
        {
            var closed = _addStockDialog;
            _addStockDialog = null;
            if (closed is not { Confirmed: true }) return;

            if (closed.ImportedHoldings.Count > 0)
            {
                if (VM.IsFundGroup)
                    VM.SyncFundHoldingsFromImport(closed.ImportedHoldings);
                else
                    VM.SyncHoldingsFromImport(closed.ImportedHoldings);
                return;
            }

            if (!string.IsNullOrEmpty(closed.SelectedInternalCode)
                && !string.IsNullOrEmpty(closed.SelectedName))
            {
                VM.AddStock(closed.SelectedInternalCode, closed.SelectedName, closed.SelectedMarket ?? "");
            }
        };
        _addStockDialog = dialog;
        dialog.Show();
        BringSecondaryToFront(dialog);
    }

    /// <summary>把二级窗提到主窗前面（主窗置顶时无 Owner 会被压在下面）。</summary>
    private static void BringSecondaryToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void BtnAlertSettings_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("请在股票行右键选择“设置预警”，可设置价格或涨跌幅上穿/下破提醒。\n\n当前已启用预警：" + VM.AlertCount,
            "预警设置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            BringSecondaryToFront(_settingsWindow);
            return;
        }

        var dialog = new SettingsWindow(
            VM.Opacity,
            VM.FontSize,
            VM.RefreshInterval,
            VM.Hotkey,
            VM.Topmost,
            VM.ShowMarketTag,
            VM.HoldingVisionProvider,
            VM.DeepSeekApiKey,
            VM.MimoApiKey,
            VM)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Topmost = false
        };

        dialog.Closed += (_, _) =>
        {
            var closed = _settingsWindow;
            _settingsWindow = null;
            if (closed is not { Confirmed: true }) return;

            VM.SaveSettings(
                closed.OpacityValue,
                closed.FontSizeValue,
                closed.RefreshInterval,
                closed.Hotkey,
                closed.IsTopmost,
                closed.ShowMarketTag,
                closed.HoldingVisionProvider,
                closed.DeepSeekApiKey,
                closed.MimoApiKey);
            SettingsChanged?.Invoke(closed.Hotkey);
        };

        _settingsWindow = dialog;
        dialog.Show();
        BringSecondaryToFront(dialog);
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
        Hide();
    }

    // 托盘退出时真正关闭窗口
    public void CloseForExit()
    {
        _isReallyClosing = true;
        Close();
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

    private void BtnCycleQuoteSource_Click(object sender, RoutedEventArgs e)
    {
        VM.CycleQuoteDataSource();
        e.Handled = true;
    }

    private void BtnSortPrice_Click(object sender, RoutedEventArgs e)
    {
        VM.TogglePriceSort();
        e.Handled = true;
    }

    private void BtnSortColumn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column })
            VM.ToggleSort(column);
        e.Handled = true;
    }

    private void BtnEditMode_Click(object sender, RoutedEventArgs e)
    {
        VM.ToggleEditMode();
        e.Handled = true;
    }

    private void BtnRowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: StockItem stock }) return;
        ConfirmAndRemoveStock(stock);
        e.Handled = true;
    }

    private void ListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        HeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
        SyncHeaderPadding();
    }

    // 表头拖拽列宽，同步到数据行
    private void ColumnResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: string column }) return;
        VM.AdjustColumnWidth(column, e.HorizontalChange);
    }

    private void ColumnResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        VM.SaveColumnWidths();
    }

    // 表头与列表共用列宽；列表出现垂直滚动条时预留同等宽度
    private void SyncHeaderPadding()
    {
        var pad = ListScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible
            ? SystemParameters.VerticalScrollBarWidth
            : 0;
        HeaderScroll.Padding = new Thickness(0, 0, pad, 0);
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
        if (_manageGroupsWindow != null)
        {
            BringSecondaryToFront(_manageGroupsWindow);
            return;
        }

        var dlg = new ManageGroupsWindow(VM)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Topmost = false
        };
        dlg.Closed += (_, _) => _manageGroupsWindow = null;
        _manageGroupsWindow = dlg;
        dlg.Show();
        BringSecondaryToFront(dlg);
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
            WindowStartupLocation = WindowStartupLocation.CenterOwner
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
        // 始终记录按下起点，避免排序等禁用拖拽时 _dragStart 残留旧坐标导致点击被误判为拖拽
        _dragStart = e.GetPosition(null);

        if (_suppressListInput || VM.SelectedStock != null || !VM.CanReorder)
        {
            _draggedItem = null;
            return;
        }

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
            DetailSheet.Close();
            return;
        }

        VM.SelectedStock = stock;
        await DetailSheet.ShowAsync(stock);
    }

    private void OnDetailSheetSuppressListInput(bool suppress)
    {
        _suppressListInput = suppress;
        if (suppress) _draggedItem = null;
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

        var headerNameText = FindVisualChild<TextBlock>(menu, "HeaderNameText");
        if (headerNameText != null)
            headerNameText.Text = stock.DisplayName;

        var headerMetaText = FindVisualChild<TextBlock>(menu, "HeaderMetaText");
        if (headerMetaText != null)
            headerMetaText.Text = $"{stock.CodeNumeric} · {stock.Market}";

        RebuildGroupSubMenu(menu, stock);

        var removeFromGroup = FindVisualChild<MenuItem>(menu, "RemoveFromGroupMenuItem");
        if (removeFromGroup != null)
            removeFromGroup.Visibility = VM.SelectedGroupId == MainViewModel.HoldingGroupId
                ? Visibility.Collapsed
                : Visibility.Visible;

        foreach (var obj in menu.Items)
        {
            if (obj is not MenuItem mi) continue;
            if (mi.Header as string is "删除股票" or "清仓")
                mi.Header = VM.IsHoldingGroupSelected ? "清仓" : "删除股票";
            if (mi.Header as string == "设置定投")
                mi.Visibility = stock.IsFund ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RebuildGroupSubMenu(ContextMenu menu, StockItem stock)
    {
        var groupSubMenu = FindVisualChild<MenuItem>(menu, "GroupSubMenu");
        if (groupSubMenu == null) return;

        groupSubMenu.Items.Clear();
        foreach (var group in VM.Groups.Where(g =>
                     g.Id != MainViewModel.HoldingGroupId
                     && !VM.HasStockInGroup(stock.Code, g.Id)))
        {
            var item = new MenuItem
            {
                Header = group.Name,
                Tag = group.Id,
                Style = (Style)FindResource("ContextMenuItemStyle"),
                Icon = new TextBlock { Text = " ", Width = 18 }
            };
            item.Click += ContextMenu_MoveToGroup;
            groupSubMenu.Items.Add(item);
        }

        groupSubMenu.IsEnabled = groupSubMenu.Items.Count > 0;
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
        ConfirmAndRemoveStock(item);
    }

    private void ConfirmAndRemoveStock(StockItem item)
    {
        // 持仓分组：只清仓，不从自选列表删除
        if (VM.IsHoldingGroupSelected)
        {
            var clear = MessageBox.Show(
                this,
                $"确定要清除「{item.DisplayName}」的持仓吗？不会从自选列表中删除。",
                "确认清仓",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (clear == MessageBoxResult.OK)
                VM.SetHolding(item, 0, 0);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"确定要从自选列表中删除「{item.DisplayName}」吗？",
            "确认删除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.OK)
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

    private void ContextMenu_MoveTop(object sender, RoutedEventArgs e)
    {
        var item = GetStockItemFromSender(sender);
        if (item == null) return;
        VM.MoveStockToTop(item);
    }

    private void ContextMenu_MoveBottom(object sender, RoutedEventArgs e)
    {
        var item = GetStockItemFromSender(sender);
        if (item == null) return;
        VM.MoveStockToBottom(item);
    }

    private void ContextMenu_SetHolding(object sender, RoutedEventArgs e)
    {
        var stock = GetStockItemFromSender(sender);
        if (stock == null) return;

        var result = ShowHoldingDialog(stock, this);
        if (result == null) return;
        VM.SetHolding(stock, result.Value.Shares, result.Value.Cost);
    }

    private void ContextMenu_SetDca(object sender, RoutedEventArgs e)
    {
        var stock = GetStockItemFromSender(sender);
        if (stock == null || !stock.IsFund) return;

        var existing = VM.GetDcaPlan(stock.Code);
        var result = ShowDcaDialog(stock, existing, this);
        if (result == null) return;
        VM.SetDcaPlan(stock.Code, result.Value.Amount, result.Value.Enabled);
    }

    private static (decimal Amount, bool Enabled)? ShowDcaDialog(StockItem stock, DcaPlan? existing, Window owner)
    {
        var dialog = new Window
        {
            Title = "设置定投",
            Width = 340,
            Height = 260,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var amountBox = CreateDialogTextBox(existing is { Amount: > 0 }
            ? existing.Amount.ToString("0.####", CultureInfo.InvariantCulture)
            : "100");
        var enabledBox = new CheckBox
        {
            Content = "启用每日定投（15:00 后按已公布净值入账）",
            IsChecked = existing?.Enabled ?? true,
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            FontSize = 12
        };
        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48)),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 8),
            Visibility = Visibility.Collapsed
        };

        var cancelBtn = CreateRoundedButton("取消", false);
        cancelBtn.Width = 75;
        cancelBtn.Height = 32;
        var okBtn = CreateRoundedButton("确定", true);
        okBtn.Width = 75;
        okBtn.Height = 32;
        okBtn.Margin = new Thickness(0, 0, 8, 0);

        (decimal Amount, bool Enabled)? result = null;
        okBtn.Click += (_, _) =>
        {
            if (!TryParseHolding(amountBox.Text, out var amount) || amount < 0)
            {
                errorText.Text = "请输入有效的定投金额";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            bool enabled = enabledBox.IsChecked == true;
            if (enabled && amount <= 0)
            {
                errorText.Text = "启用定投时金额须大于 0";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            result = (amount, enabled && amount > 0);
            dialog.DialogResult = true;
        };
        cancelBtn.Click += (_, _) => dialog.DialogResult = false;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = $"设置 {stock.DisplayName} 定投",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
        });
        stack.Children.Add(new TextBlock { Text = "每日定投金额", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)) });
        stack.Children.Add(amountBox);
        stack.Children.Add(enabledBox);
        stack.Children.Add(errorText);
        stack.Children.Add(buttons);

        dialog.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF8, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xD0, 0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = stack
        };
        dialog.MouseLeftButtonDown += (_, _) => dialog.DragMove();
        dialog.Loaded += (_, _) =>
        {
            amountBox.Focus();
            amountBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? result : null;
    }

    private void ContextMenu_SetAlert(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not StockItem stock) return;
        var panel = new StackPanel { Margin = new Thickness(18) };
        var metric = new ComboBox { ItemsSource = new[] { "价格", "涨跌幅" }, SelectedIndex = 0, Height = 28 };
        var direction = new ComboBox { ItemsSource = new[] { "上穿", "下破" }, SelectedIndex = 0, Height = 28, Margin = new Thickness(0, 8, 0, 0) };
        var value = new TextBox { Height = 28, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = $"设置 {stock.DisplayName} 预警", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0x36, 0x4A)) });
        panel.Children.Add(new TextBlock { Text = "指标", Margin = new Thickness(0, 12, 0, 3), Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB6)) }); panel.Children.Add(metric);
        panel.Children.Add(direction); panel.Children.Add(value);
        var win = new Window { Title = "设置预警", Content = panel, Width = 280, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Background = Brushes.White, ResizeMode = ResizeMode.NoResize };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "保存", Width = 64, Height = 28, Margin = new Thickness(6, 0, 0, 0) }; var cancel = new Button { Content = "取消", Width = 64, Height = 28 };
        ok.Click += (_, _) => { if (!decimal.TryParse(value.Text, out var threshold)) { MessageBox.Show("请输入有效阈值"); return; } VM.AddAlert(stock, metric.SelectedIndex == 0 ? AlertMetric.Price : AlertMetric.ChangePercent, direction.SelectedIndex == 0 ? AlertDirection.Above : AlertDirection.Below, threshold); win.DialogResult = true; };
        cancel.Click += (_, _) => win.Close(); buttons.Children.Add(cancel); buttons.Children.Add(ok); panel.Children.Add(buttons); win.ShowDialog();
    }

    private void ContextMenu_MoveToGroup(object sender, RoutedEventArgs e)
    {
        var stock = GetStockItemFromSender(sender);
        if (stock == null) return;
        if (sender is not MenuItem item || item.Tag is not string groupId) return;
        VM.MoveStockToGroup(stock, groupId);
    }

    private void ContextMenu_RemoveFromGroup(object sender, RoutedEventArgs e)
    {
        var stock = GetStockItemFromSender(sender);
        if (stock == null) return;

        var result = MessageBox.Show(
            this,
            $"确定要将「{stock.DisplayName}」从当前分组移出吗？",
            "确认移出",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.OK && !VM.RemoveStockFromCurrentGroup(stock))
            MessageBox.Show(this, "当前没有可移入的其他分组", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private static (decimal Shares, decimal Cost)? ShowHoldingDialog(StockItem stock, Window owner)
    {
        var isFund = stock.IsFund;
        var dialog = new Window
        {
            Title = "持仓设置",
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var costBox = CreateHoldingTextBox(stock.HoldingCost > 0
            ? stock.HoldingCost.ToString("0.####", CultureInfo.InvariantCulture)
            : "");
        var sharesBox = CreateHoldingTextBox(stock.HoldingShares > 0
            ? stock.HoldingShares.ToString("0.####", CultureInfo.InvariantCulture)
            : "");
        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48)),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };

        var cancelBtn = CreateHoldingActionButton("取消", isPrimary: false);
        cancelBtn.Margin = new Thickness(0, 0, 10, 0);

        var clearBtn = CreateHoldingActionButton("清仓", isPrimary: false);
        clearBtn.Margin = new Thickness(0, 0, 10, 0);
        clearBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48));

        var saveBtn = CreateHoldingActionButton("保存", isPrimary: true, withSaveIcon: true);

        (decimal Shares, decimal Cost)? result = null;
        saveBtn.Click += (_, _) =>
        {
            if (!TryParseHolding(sharesBox.Text, out var shares) || !TryParseHolding(costBox.Text, out var cost))
            {
                errorText.Text = isFund ? "请输入有效的持仓成本单价和持有份额" : "请输入有效的持仓成本价和持有数量";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            // 份额为 0 即不持仓，成本价可忽略；有份额时成本价必须 > 0
            if (shares > 0 && cost <= 0)
            {
                errorText.Text = isFund ? "请填写有效的持仓成本单价" : "请填写有效的持仓成本价";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            result = (shares, shares > 0 ? cost : 0);
            dialog.DialogResult = true;
        };
        clearBtn.Click += (_, _) =>
        {
            result = (0, 0);
            dialog.DialogResult = true;
        };
        cancelBtn.Click += (_, _) => dialog.DialogResult = false;

        var costLabel = isFund ? "持仓成本单价" : "持仓成本价";
        var sharesLabel = isFund ? "持有份额" : "持有数量";
        var subtitle = $"{stock.DisplayName} ({stock.CodeNumeric})";

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 22)
        };
        header.Children.Add(CreateHoldingWalletIcon());
        var headerText = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(new TextBlock
        {
            Text = "持仓设置",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
        });
        headerText.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12.5,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
        });
        header.Children.Add(headerText);

        var fields = new StackPanel();
        fields.Children.Add(CreateHoldingFieldLabel(costLabel));
        fields.Children.Add(costBox);
        fields.Children.Add(CreateHoldingFieldLabel(sharesLabel, topMargin: 14));
        fields.Children.Add(sharesBox);
        fields.Children.Add(errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(clearBtn);
        buttons.Children.Add(saveBtn);

        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        stack.Children.Add(header);
        stack.Children.Add(fields);
        stack.Children.Add(buttons);

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF1)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(10),
            Child = stack,
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.18,
                Color = Color.FromRgb(0x15, 0x23, 0x3B)
            }
        };

        dialog.Content = card;
        dialog.MouseLeftButtonDown += (_, _) => dialog.DragMove();
        dialog.Loaded += (_, _) =>
        {
            costBox.Focus();
            costBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? result : null;
    }

    private static TextBlock CreateHoldingFieldLabel(string text, double topMargin = 0) => new()
    {
        Text = text,
        FontSize = 12,
        Margin = new Thickness(2, topMargin, 0, 6),
        Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
    };

    private static FrameworkElement CreateHoldingWalletIcon()
    {
        var canvas = new Canvas { Width = 22, Height = 22 };
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M4,7.5 L18,7.5 A2,2 0 0 1 20,9.5 L20,16.5 A2,2 0 0 1 18,18.5 L4,18.5 A2,2 0 0 1 2,16.5 L2,5.5 A2,2 0 0 1 4,3.5 L14.5,3.5 A2,2 0 0 1 16.5,5.5 L16.5,7.5"),
            Stroke = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            StrokeThickness = 1.6,
            Fill = Brushes.Transparent,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        });
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M15.5,13 L20,13"),
            Stroke = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        var clasp = new Ellipse
        {
            Width = 2.2,
            Height = 2.2,
            Fill = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
        };
        Canvas.SetLeft(clasp, 14.2);
        Canvas.SetTop(clasp, 11.9);
        canvas.Children.Add(clasp);
        return new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF5, 0xF8)),
            Child = new Viewbox
            {
                Width = 18,
                Height = 18,
                Child = canvas,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    private static TextBox CreateHoldingTextBox(string text)
    {
        var box = new TextBox
        {
            Text = text,
            Height = 42,
            FontSize = 14,
            Padding = new Thickness(14, 0, 14, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };

        var template = new ControlTemplate(typeof(TextBox));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);

        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.Name = "PART_ContentHost";
        scroll.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(scroll);
        template.VisualTree = border;
        box.Template = template;
        return box;
    }

    private static System.Windows.Controls.Button CreateHoldingActionButton(string text, bool isPrimary, bool withSaveIcon = false)
    {
        var bg = isPrimary
            ? new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
            : new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF5));
        var fg = isPrimary
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
        var hoverBg = isPrimary
            ? new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B))
            : new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));

        object content = text;
        if (withSaveIcon)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M3.5,3.5 L12.2,3.5 L14.5,5.8 L14.5,14.5 L3.5,14.5 Z M5.2,3.5 L5.2,7.4 L11.2,7.4 L11.2,3.5 M5.8,10.2 L12.2,10.2 L12.2,14.5 L5.8,14.5 Z"),
                Stroke = Brushes.White,
                StrokeThickness = 1.35,
                Fill = Brushes.Transparent,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(icon);
            row.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold
            });
            content = row;
        }

        var btn = new System.Windows.Controls.Button
        {
            Content = content,
            Background = bg,
            Foreground = fg,
            BorderThickness = new Thickness(0),
            FontSize = 13.5,
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = Cursors.Hand,
            MinWidth = isPrimary ? 96 : 84,
            Height = 40,
            Padding = new Thickness(18, 0, 18, 0)
        };

        if (isPrimary)
        {
            btn.Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.28,
                Color = Color.FromRgb(0x0F, 0x17, 0x2A)
            };
        }

        var template = new ControlTemplate(typeof(System.Windows.Controls.Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding(nameof(Control.Background))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, hoverBg));
        template.Triggers.Add(hover);
        btn.Template = template;
        return btn;
    }

    private static TextBox CreateDialogTextBox(string text) => new()
    {
        Text = text,
        Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5)),
        Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
        CaretBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
        BorderThickness = new Thickness(1),
        Height = 30,
        Padding = new Thickness(8, 0, 8, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
        FontSize = 13
    };

    private static bool TryParseHolding(string text, out decimal value)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            value = 0;
            return true;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
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
            && GetContextMenu(menuItem) is ContextMenu contextMenu
            && contextMenu.PlacementTarget is FrameworkElement element)
        {
            return element.Tag as StockItem;
        }
        return null;
    }

    private static ContextMenu? GetContextMenu(DependencyObject current)
    {
        while (current != null)
        {
            if (current is ContextMenu contextMenu)
                return contextMenu;

            current = LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
