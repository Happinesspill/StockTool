using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        var dialog = new AddStockDialog(_client, VM.IsFundGroup) { Owner = this };
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

    private void BtnAlertSettings_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("请在股票行右键选择“设置预警”，可设置价格或涨跌幅上穿/下破提醒。\n\n当前已启用预警：" + VM.AlertCount,
            "预警设置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void OpenSettings()
    {
        var dialog = new SettingsWindow(VM.Opacity, VM.FontSize, VM.RefreshInterval, VM.Hotkey, VM.Topmost, VM.ShowMarketTag, VM)
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

    private static (decimal Shares, decimal Cost)? ShowHoldingDialog(StockItem stock, Window owner)
    {
        var dialog = new Window
        {
            Title = "设置持仓",
            Width = 340,
            Height = 220,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var sharesBox = CreateDialogTextBox(stock.HoldingShares > 0 ? stock.HoldingShares.ToString("0.####", CultureInfo.InvariantCulture) : "");
        var costBox = CreateDialogTextBox(stock.HoldingCost > 0 ? stock.HoldingCost.ToString("0.####", CultureInfo.InvariantCulture) : "");
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

        (decimal Shares, decimal Cost)? result = null;
        okBtn.Click += (_, _) =>
        {
            if (!TryParseHolding(sharesBox.Text, out var shares) || !TryParseHolding(costBox.Text, out var cost))
            {
                errorText.Text = "请输入有效的持仓数量和成本价";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            if ((shares == 0 && cost > 0) || (shares > 0 && cost == 0))
            {
                errorText.Text = "清空持仓时数量和成本价都填 0 或留空";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            result = (shares, cost);
            dialog.DialogResult = true;
        };
        cancelBtn.Click += (_, _) => dialog.DialogResult = false;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = $"设置 {stock.DisplayName} 持仓",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
        });
        stack.Children.Add(new TextBlock { Text = "持仓数量", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)) });
        stack.Children.Add(sharesBox);
        stack.Children.Add(new TextBlock { Text = "成本价", FontSize = 12, Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)) });
        stack.Children.Add(costBox);
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
            sharesBox.Focus();
            sharesBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? result : null;
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
