using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StockTool.ViewModels;

public class IndexOptionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; init; } = string.Empty;
    public string DisplayCode { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CheckMark));
        }
    }

    public string CheckMark => IsSelected ? "✓" : "○";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
