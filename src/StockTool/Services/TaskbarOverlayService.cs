using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using StockTool.ViewModels;
using StockTool.Views;

namespace StockTool.Services;

/// <summary>按屏幕维护任务栏透明行情叠层。</summary>
public sealed class TaskbarOverlayService : IDisposable
{
    private readonly ObservableCollection<TaskbarStockViewItem> _items;
    private readonly Func<double> _getSparklineWidth;
    private readonly Func<double> _getClockGap;
    private readonly Func<double> _getVerticalOffset;
    private readonly Func<Brush> _getTextBrush;
    private readonly Action<Core.Models.StockItem>? _onStockDoubleClick;
    private readonly Dictionary<int, TaskbarOverlayWindow> _windows = new();
    private bool _disposed;

    public TaskbarOverlayService(
        ObservableCollection<TaskbarStockViewItem> items,
        Func<double> getSparklineWidth,
        Func<Brush> getTextBrush,
        Action<Core.Models.StockItem>? onStockDoubleClick = null,
        Func<double>? getClockGap = null,
        Func<double>? getVerticalOffset = null)
    {
        _items = items;
        _getSparklineWidth = getSparklineWidth;
        _getClockGap = getClockGap ?? (() => 4);
        _getVerticalOffset = getVerticalOffset ?? (() => 0);
        _getTextBrush = getTextBrush;
        _onStockDoubleClick = onStockDoubleClick;
        _items.CollectionChanged += OnItemsChanged;
        foreach (var item in _items)
            HookItem(item);

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        TaskbarDiagLog.Info($"service ctor items={_items.Count} log={TaskbarDiagLog.LogPath}");
    }

    public void Start()
    {
        TaskbarDiagLog.Info("service Start");
        Application.Current?.Dispatcher.Invoke(SyncWindows);
    }

    public void Refresh()
    {
        if (_disposed) return;
        Application.Current?.Dispatcher.Invoke(SyncWindows);
    }

    /// <summary>主窗激活后把叠层重新顶到任务栏之上，避免“股票不见了”。</summary>
    public void EnsureVisible()
    {
        if (_disposed)
        {
            TaskbarDiagLog.Warn("EnsureVisible skipped: disposed");
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            TaskbarDiagLog.Warn("EnsureVisible skipped: no dispatcher");
            return;
        }

        void Apply()
        {
            if (_disposed) return;
            TaskbarDiagLog.Info($"EnsureVisible apply wins={_windows.Count} items={_items.Count}");
            foreach (var win in _windows.Values)
            {
                try { win.EnsureTopmost(); }
                catch (Exception ex)
                {
                    TaskbarDiagLog.Error($"EnsureVisible win[{win.ScreenIndex}] failed", ex);
                }
            }
        }

        dispatcher.BeginInvoke(Apply, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TaskbarDiagLog.Trace(
            $"items CollectionChanged action={e.Action} old={e.OldItems?.Count ?? 0} new={e.NewItems?.Count ?? 0} total={_items.Count}");

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
        // 行情刷新只靠绑定更新，不重排窗口，避免点击闪烁
        if (e.PropertyName is nameof(Core.Models.StockItem.DisplayName))
            Application.Current?.Dispatcher.BeginInvoke(RepositionAll);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        TaskbarDiagLog.Trace("DisplaySettingsChanged → Refresh");
        Refresh();
    }

    private void SyncWindows()
    {
        if (_disposed) return;

        double sparkleWidth = _getSparklineWidth();
        var textBrush = _getTextBrush();
        var byScreen = _items
            .GroupBy(i => i.Screen)
            .ToDictionary(g => g.Key, g => g.ToList());

        string itemSummary = string.Join(",", _items.Select(i => $"{i.Stock.Code}@s{i.Screen}"));
        TaskbarDiagLog.Info(
            $"SyncWindows begin items={_items.Count} [{itemSummary}] wins={_windows.Count} sparkW={sparkleWidth:0.#}");

        foreach (var screen in byScreen.Keys.ToList())
        {
            if (!TaskbarGeometry.TryGetBand(screen, out _))
            {
                TaskbarDiagLog.Warn($"SyncWindows drop screen={screen}: TryGetBand failed");
                byScreen.Remove(screen);
            }
        }

        foreach (var kv in _windows.ToList())
        {
            if (!byScreen.ContainsKey(kv.Key))
            {
                TaskbarDiagLog.Trace($"SyncWindows Close win[{kv.Key}] (screen gone)");
                kv.Value.Close();
                _windows.Remove(kv.Key);
            }
        }

        foreach (var (screen, list) in byScreen)
        {
            if (!_windows.TryGetValue(screen, out var win))
            {
                TaskbarDiagLog.Info($"SyncWindows create win[{screen}] for {list.Count} stocks");
                win = new TaskbarOverlayWindow(screen);
                win.StockDoubleClicked += OnWindowStockDoubleClicked;
                _windows[screen] = win;
            }

            win.SparklineWidth = sparkleWidth;
            win.ClockGap = _getClockGap();
            win.VerticalOffset = _getVerticalOffset();
            win.ApplyTextBrush(textBrush);
            win.SetItems(list);
            win.PlaceOnTaskbar();
        }

        if (_items.Count == 0)
        {
            TaskbarDiagLog.Trace($"SyncWindows items=0 → Close all wins={_windows.Count}");
            foreach (var win in _windows.Values)
                win.Close();
            _windows.Clear();
        }

        TaskbarDiagLog.Info($"SyncWindows end wins={_windows.Count}");
    }

    private void RepositionAll()
    {
        if (_disposed) return;
        TaskbarDiagLog.Info($"RepositionAll wins={_windows.Count}");
        double sparkleWidth = _getSparklineWidth();
        var textBrush = _getTextBrush();
        foreach (var win in _windows.Values)
        {
            win.SparklineWidth = sparkleWidth;
            win.ClockGap = _getClockGap();
            win.VerticalOffset = _getVerticalOffset();
            win.ApplyTextBrush(textBrush);
            win.UpdateLayout();
            win.PlaceOnTaskbar();
        }
    }

    private void OnWindowStockDoubleClicked(Core.Models.StockItem stock)
    {
        TaskbarDiagLog.Info($"service DoubleClick callback code={stock.Code} hasHandler={_onStockDoubleClick != null}");
        try
        {
            _onStockDoubleClick?.Invoke(stock);
        }
        catch (Exception ex)
        {
            TaskbarDiagLog.Error("service DoubleClick callback threw", ex);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TaskbarDiagLog.Trace("service Dispose");

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _items.CollectionChanged -= OnItemsChanged;
        foreach (var item in _items)
            UnhookItem(item);

        foreach (var win in _windows.Values)
        {
            win.StockDoubleClicked -= OnWindowStockDoubleClicked;
            try { win.Close(); } catch { /* ignore */ }
        }
        _windows.Clear();
    }
}
