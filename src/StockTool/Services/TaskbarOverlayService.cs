using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using StockTool.ViewModels;
using StockTool.Views;

namespace StockTool.Services;

/// <summary>按屏幕维护任务栏透明行情叠层。</summary>
public sealed class TaskbarOverlayService : IDisposable
{
    private readonly ObservableCollection<TaskbarStockViewItem> _items;
    private readonly Dictionary<int, TaskbarOverlayWindow> _windows = new();
    private bool _disposed;

    public TaskbarOverlayService(ObservableCollection<TaskbarStockViewItem> items)
    {
        _items = items;
        _items.CollectionChanged += OnItemsChanged;
        foreach (var item in _items)
            HookItem(item);

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Start()
    {
        Application.Current?.Dispatcher.Invoke(SyncWindows);
    }

    public void Refresh()
    {
        if (_disposed) return;
        Application.Current?.Dispatcher.Invoke(SyncWindows);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (TaskbarStockViewItem item in e.OldItems)
                UnhookItem(item);
        }

        if (e.NewItems != null)
        {
            foreach (TaskbarStockViewItem item in e.NewItems)
                HookItem(item);
        }

        Refresh();
    }

    private void HookItem(TaskbarStockViewItem item)
    {
        item.PropertyChanged += OnItemPropertyChanged;
        item.Stock.PropertyChanged += OnStockPropertyChanged;
    }

    private void UnhookItem(TaskbarStockViewItem item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
        item.Stock.PropertyChanged -= OnStockPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskbarStockViewItem.Screen) or nameof(TaskbarStockViewItem.ScreenLabel))
            Refresh();
    }

    private void OnStockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 行情/分时变化时仅重排位置（内容由绑定刷新）
        if (e.PropertyName is nameof(Core.Models.StockItem.IntradayPoints)
            or nameof(Core.Models.StockItem.PriceText)
            or nameof(Core.Models.StockItem.TaskbarPriceText)
            or nameof(Core.Models.StockItem.ChangePercentText)
            or nameof(Core.Models.StockItem.DisplayName))
        {
            Application.Current?.Dispatcher.BeginInvoke(RepositionAll);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Refresh();

    private void SyncWindows()
    {
        if (_disposed) return;

        var byScreen = _items
            .GroupBy(i => i.Screen)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var screen in byScreen.Keys.ToList())
        {
            if (!TaskbarGeometry.TryGetBand(screen, out _))
                byScreen.Remove(screen);
        }

        foreach (var kv in _windows.ToList())
        {
            if (!byScreen.ContainsKey(kv.Key))
            {
                kv.Value.Close();
                _windows.Remove(kv.Key);
            }
        }

        foreach (var (screen, list) in byScreen)
        {
            if (!_windows.TryGetValue(screen, out var win))
            {
                win = new TaskbarOverlayWindow(screen);
                _windows[screen] = win;
            }

            win.SetItems(list);
            win.PlaceOnTaskbar();
        }

        if (_items.Count == 0)
        {
            foreach (var win in _windows.Values)
                win.Close();
            _windows.Clear();
        }
    }

    private void RepositionAll()
    {
        if (_disposed) return;
        foreach (var win in _windows.Values)
        {
            win.UpdateLayout();
            win.PlaceOnTaskbar();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _items.CollectionChanged -= OnItemsChanged;
        foreach (var item in _items)
            UnhookItem(item);

        foreach (var win in _windows.Values)
        {
            try { win.Close(); } catch { /* ignore */ }
        }
        _windows.Clear();
    }
}
