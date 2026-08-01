using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StockTool.Core.Models;

public class IndexItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _code = string.Empty;
    private string _displayCode = string.Empty;
    private decimal _price;
    private decimal _change;
    private decimal _changePercent;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Code
    {
        get => _code;
        set { _code = value; OnPropertyChanged(); }
    }

    public string DisplayCode
    {
        get => _displayCode;
        set { _displayCode = value; OnPropertyChanged(); }
    }

    public decimal Price
    {
        get => _price;
        set { _price = value; OnPropertyChanged(); }
    }

    public decimal Change
    {
        get => _change;
        set { _change = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUp)); }
    }

    public decimal ChangePercent
    {
        get => _changePercent;
        set { _changePercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUp)); }
    }

    public bool IsUp => _changePercent >= 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
