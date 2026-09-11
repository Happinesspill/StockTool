using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using StockTool.Core.Models;
using StockTool.ViewModels;

namespace StockTool.Views;

public partial class ManageGroupsWindow : Window
{
    private readonly MainViewModel _vm;
    private Point _dragStart;
    private WatchlistGroup? _draggedGroup;
    private IndexItem? _draggedIndex;
    private bool _committed;

    public ManageGroupsWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = _vm;
        InitializeComponent();
        // 管理页只展示可编辑分组，不含自选、持仓
        GroupList.ItemsSource = new ListCollectionView(_vm.Groups)
        {
            Filter = o => o is WatchlistGroup g && !MainViewModel.IsFixedGroup(g)
        };
        _vm.BeginManageGroupsEdit();
        Closing += (_, _) =>
        {
            if (!_committed)
                _vm.CancelManageGroupsEdit();
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        // 分组/指数卡片内拖拽排序时不移动窗口
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<ListBox>(source) is ListBox list &&
            (list == GroupList || list == IndexCardList))
            return;
        DragMove();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        _committed = true;
        _vm.CommitManageGroupsEdit();
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptText("新建分组", "");
        if (name == null) return;
        if (_vm.AddGroup(name) == null)
            MessageBox.Show(this, "创建失败（名称无效或已存在）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void IndexCardList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
        {
            _draggedIndex = null;
            return;
        }

        _dragStart = e.GetPosition(null);
        _draggedIndex = FindIndexAtMouse(e);
    }

    private void IndexCardList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedIndex == null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var item = _draggedIndex;
            _draggedIndex = null;
            DragDrop.DoDragDrop(IndexCardList, item, DragDropEffects.Move);
        }
    }

    private void IndexCardList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(IndexItem)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void IndexCardList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(IndexItem))) return;
        var dragged = e.Data.GetData(typeof(IndexItem)) as IndexItem;
        if (dragged == null) return;

        var target = FindIndexAtMouse(e);
        if (target == null || target == dragged) return;

        int oldIndex = _vm.SelectedHomeIndices.IndexOf(dragged);
        int newIndex = _vm.SelectedHomeIndices.IndexOf(target);
        _vm.ReorderHomeIndex(oldIndex, newIndex);
    }

    private void IndexCardList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        e.Handled = true;
    }

    private IndexItem? FindIndexAtMouse(RoutedEventArgs e)
    {
        Point pos;
        if (e is MouseEventArgs me)
            pos = me.GetPosition(IndexCardList);
        else if (e is DragEventArgs de)
            pos = de.GetPosition(IndexCardList);
        else
            return null;

        var element = IndexCardList.InputHitTest(pos) as DependencyObject;
        while (element != null)
        {
            if (element is FrameworkElement fe && fe.DataContext is IndexItem item)
                return item;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void GroupList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 点在重命名/删除按钮上时不启动拖拽
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
        {
            _draggedGroup = null;
            return;
        }

        _dragStart = e.GetPosition(null);
        var group = FindGroupAtMouse(e);
        // 自选、持仓固定，不可拖拽
        if (group != null && IsPinnedGroup(group))
        {
            _draggedGroup = null;
            return;
        }
        _draggedGroup = group;
    }

    private void GroupList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedGroup == null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var item = _draggedGroup;
            _draggedGroup = null;
            DragDrop.DoDragDrop(GroupList, item, DragDropEffects.Move);
        }
    }

    private void GroupList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(WatchlistGroup)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var dragged = e.Data.GetData(typeof(WatchlistGroup)) as WatchlistGroup;
        var target = FindGroupAtMouse(e);
        if (dragged == null || IsPinnedGroup(dragged) || (target != null && IsPinnedGroup(target)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void GroupList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(WatchlistGroup))) return;
        var dragged = e.Data.GetData(typeof(WatchlistGroup)) as WatchlistGroup;
        if (dragged == null || IsPinnedGroup(dragged)) return;

        var target = FindGroupAtMouse(e);
        if (target == null || target == dragged || IsPinnedGroup(target)) return;

        int oldIndex = _vm.Groups.IndexOf(dragged);
        int newIndex = _vm.Groups.IndexOf(target);
        _vm.MoveGroup(oldIndex, newIndex);
    }

    private bool IsPinnedGroup(WatchlistGroup group)
    {
        int idx = _vm.Groups.IndexOf(group);
        return idx >= 0 && idx < MainViewModel.FixedGroupCount;
    }

    private void GroupList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        e.Handled = true;
    }

    private WatchlistGroup? FindGroupAtMouse(RoutedEventArgs e)
    {
        Point pos;
        if (e is MouseEventArgs me)
            pos = me.GetPosition(GroupList);
        else if (e is DragEventArgs de)
            pos = de.GetPosition(GroupList);
        else
            return null;

        var element = GroupList.InputHitTest(pos) as DependencyObject;
        while (element != null)
        {
            if (element is FrameworkElement fe && fe.DataContext is WatchlistGroup group)
                return group;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void BtnToggleIndex_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string code) return;
        string? err = _vm.ToggleHomeIndex(code);
        if (err != null)
            MessageBox.Show(this, err, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        e.Handled = true;
    }

    private void BtnRemoveIndex_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string code) return;
        string? err = _vm.RemoveHomeIndex(code);
        if (err != null)
            MessageBox.Show(this, err, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        e.Handled = true;
    }

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not WatchlistGroup group) return;
        if (group.Id == MainViewModel.HoldingGroupId)
        {
            MessageBox.Show(this, "「持仓」分组不可重命名", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string? name = PromptText("重命名分组", group.Name);
        if (name == null) return;
        if (!_vm.RenameGroup(group.Id, name))
            MessageBox.Show(this, "重命名失败（名称无效或已存在）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not WatchlistGroup group) return;
        if (group.Id == MainViewModel.HoldingGroupId)
        {
            MessageBox.Show(this, "「持仓」分组不可删除", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (group.Name == MainViewModel.DefaultGroupName)
        {
            MessageBox.Show(this, "「自选」分组不可删除", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"删除分组「{group.Name}」？其中的股票将移入「自选」。",
            "确认删除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        if (!_vm.DeleteGroup(group.Id))
            MessageBox.Show(this, "删除失败", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string? PromptText(string title, string initial)
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
            Text = initial,
            FontSize = 14,
            Margin = new Thickness(16, 12, 16, 8),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        box.SelectAll();

        var ok = new Button { Content = "确定", Width = 72, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 72, Height = 30 };
        ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { result = box.Text; dialog.DialogResult = true; }
            if (e.Key == Key.Escape) dialog.DialogResult = false;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new Border
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
        dialog.Content = root;
        dialog.Loaded += (_, _) => box.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }
}
