using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using StockTool.ViewModels;

namespace StockTool.Views;

public partial class AlertWindow : Window
{
    private readonly MainViewModel _vm;

    public AlertWindow(MainViewModel viewModel)
    {
        _vm = viewModel;
        InitializeComponent();
        DataContext = _vm;

        _vm.RebuildAlertItems();
        _vm.AlertItems.CollectionChanged += OnAlertItemsChanged;
        UpdateEmptyHint();

        Closed += (_, _) => _vm.AlertItems.CollectionChanged -= OnAlertItemsChanged;
    }

    private void OnAlertItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyHint();

    private void UpdateEmptyHint()
    {
        TxtAlertEmpty.Visibility = _vm.AlertItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BtnDeleteAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AlertRuleViewItem item })
            _vm.RemoveAlert(item.Rule);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        // 点在按钮/复选框上时不拖动窗口
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) != null) return;
        DragMove();
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T target) return target;
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : System.Windows.LogicalTreeHelper.GetParent(node);
        }

        return null;
    }
}
