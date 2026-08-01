using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StockTool.Core.Models;
using StockTool.ViewModels;

namespace StockTool.Views;

public partial class ManageGroupsWindow : Window
{
    private readonly MainViewModel _vm;

    public ManageGroupsWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = _vm;
        InitializeComponent();
        GroupList.ItemsSource = _vm.Groups;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

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
        string? name = PromptText("重命名分组", group.Name);
        if (name == null) return;
        if (!_vm.RenameGroup(group.Id, name))
            MessageBox.Show(this, "重命名失败（名称无效或已存在）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not WatchlistGroup group) return;
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
