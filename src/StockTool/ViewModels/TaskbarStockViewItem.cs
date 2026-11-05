using System.ComponentModel;
using System.Runtime.CompilerServices;
using StockTool.Core.Models;

namespace StockTool.ViewModels;

/// <summary>管理页 / 任务栏叠层共用的任务栏股票项。</summary>
public sealed class TaskbarStockViewItem : INotifyPropertyChanged
{
    private int _screen = 1;

    public TaskbarStockViewItem(StockItem stock, int screen)
    {
        Stock = stock;
        _screen = screen < 1 ? 1 : screen;
    }

    public StockItem Stock { get; }

    public int Screen
    {
        get => _screen;
        set
        {
            int next = value < 1 ? 1 : value;
            if (_screen == next) return;
            _screen = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScreenLabel));
        }
    }

    public string ScreenLabel => $"屏幕{_screen}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
