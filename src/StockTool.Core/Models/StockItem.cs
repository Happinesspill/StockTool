using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StockTool.Core.Models;

public class StockItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _customName = string.Empty;
    private string _code = string.Empty;
    private string _market = string.Empty;
    private string _groupId = string.Empty;
    private decimal _currentPrice;
    private decimal _changePercent;
    private decimal _yesterdayClose;
    private decimal _open;
    private decimal _high;
    private decimal _low;
    private decimal _volume;
    private decimal _turnover;
    private List<decimal> _intradayPoints = [];
    private List<decimal> _intradayAvgPoints = [];
    private List<KlineItem> _klineData = [];
    /// <summary>0=分时（当日走势），其余为东财 klt：5/15/30/60/101/102</summary>
    private int _klinePeriod = 0;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    public string CustomName
    {
        get => _customName;
        set { _customName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(_customName) ? _name : _customName;

    public string Code
    {
        get => _code;
        set { _code = value; OnPropertyChanged(); OnPropertyChanged(nameof(CodeNumeric)); }
    }

    public string CodeNumeric => _code.Length > 2 ? _code[2..] : _code;

    public string Market
    {
        get => _market;
        set { _market = value; OnPropertyChanged(); }
    }

    public string GroupId
    {
        get => _groupId;
        set { _groupId = value; OnPropertyChanged(); }
    }

    public decimal CurrentPrice
    {
        get => _currentPrice;
        set { _currentPrice = value; OnPropertyChanged(); }
    }

    public decimal ChangePercent
    {
        get => _changePercent;
        set { _changePercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUp)); }
    }

    public bool IsUp => _changePercent >= 0;

    public decimal YesterdayClose
    {
        get => _yesterdayClose;
        set { _yesterdayClose = value; OnPropertyChanged(); }
    }

    public decimal Open
    {
        get => _open;
        set { _open = value; OnPropertyChanged(); }
    }

    public decimal High
    {
        get => _high;
        set { _high = value; OnPropertyChanged(); }
    }

    public decimal Low
    {
        get => _low;
        set { _low = value; OnPropertyChanged(); }
    }

    public decimal Volume
    {
        get => _volume;
        set { _volume = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumeText)); }
    }

    public decimal Turnover
    {
        get => _turnover;
        set { _turnover = value; OnPropertyChanged(); OnPropertyChanged(nameof(TurnoverText)); }
    }

    public string VolumeText
    {
        get
        {
            if (_volume >= 1_0000_0000) return $"{_volume / 1_0000_0000:F2}亿手";
            if (_volume >= 1_0000) return $"{_volume / 1_0000:F2}万手";
            return $"{_volume:F0}手";
        }
    }

    public string TurnoverText
    {
        get
        {
            if (_turnover >= 1_0000_0000) return $"{_turnover / 1_0000_0000:F2}亿";
            if (_turnover >= 1_0000) return $"{_turnover / 1_0000:F2}万";
            return $"{_turnover:F2}";
        }
    }

    public List<decimal> IntradayPoints
    {
        get => _intradayPoints;
        set { _intradayPoints = value; OnPropertyChanged(); }
    }

    public List<decimal> IntradayAvgPoints
    {
        get => _intradayAvgPoints;
        set { _intradayAvgPoints = value; OnPropertyChanged(); }
    }

    public List<KlineItem> KlineData
    {
        get => _klineData;
        set { _klineData = value; OnPropertyChanged(); }
    }

    public int KlinePeriod
    {
        get => _klinePeriod;
        set { _klinePeriod = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
