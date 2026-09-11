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
    private decimal _turnoverRate;
    private decimal _volumeRatio;
    private decimal _speed;
    private decimal _totalMarketValue;
    private decimal _floatMarketValue;
    private decimal _holdingShares;
    private decimal _holdingCost;
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
        set { _code = value; OnPropertyChanged(); OnPropertyChanged(nameof(CodeNumeric)); OnPropertyChanged(nameof(IsFund)); OnPropertyChanged(nameof(PriceText)); }
    }

    public string CodeNumeric => _code.Length > 2 ? _code[2..] : _code;

    public string Market
    {
        get => _market;
        set { _market = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFund)); OnPropertyChanged(nameof(PriceText)); }
    }

    // 场外基金走天天基金估值接口；场内 ETF/LOF 与股票一样走行情接口
    public bool IsFund =>
        !IsExchangeFund
        && (string.Equals(_market, "基金", StringComparison.OrdinalIgnoreCase)
            || _code.StartsWith("FD", StringComparison.OrdinalIgnoreCase));

    /// <summary>是否场内 ETF/LOF（沪 5 字头场内基金、深 159/16x）</summary>
    public bool IsExchangeFund => IsExchangeFundCode(_code);

    public static bool IsExchangeFundCode(string? code) => ExchangeMarketPrefix(code) != null;

    /// <summary>场内基金代码判定：返回所属市场前缀 SH/SZ，非场内基金返回 null</summary>
    public static string? ExchangeMarketPrefix(string? code)
    {
        string digits = NumericPartOf(code);
        if (digits.Length != 6) return null;

        // 取前三位做区段判定，顺带校验是否全数字
        int head = 0;
        foreach (char c in digits)
        {
            if (c is < '0' or > '9') return null;
            head = head * 10 + (c - '0');
        }
        head /= 1000;

        // 沪市：500-523（封闭/LOF/ETF）、560-563（ETF）、588-589（科创 ETF）
        if ((head >= 500 && head <= 523) || (head >= 560 && head <= 563) || (head >= 588 && head <= 589))
            return "SH";
        // 深市：159（ETF）、160-169（LOF）
        if (head == 159 || (head >= 160 && head <= 169))
            return "SZ";
        return null;
    }

    /// <summary>归一化为行情接口代码：FD513310 / 513310 → SH513310</summary>
    public static string ToExchangeCode(string? code)
    {
        string? prefix = ExchangeMarketPrefix(code);
        return prefix == null ? code ?? string.Empty : prefix + NumericPartOf(code);
    }

    /// <summary>去掉 FD/SH/SZ/HK 前缀，取纯数字代码</summary>
    public static string NumericPartOf(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        string s = code.Trim().ToUpperInvariant();
        if (s.Length > 2 && (s.StartsWith("FD") || s.StartsWith("SH") || s.StartsWith("SZ") || s.StartsWith("HK")))
            s = s[2..];
        return s;
    }

    public string GroupId
    {
        get => _groupId;
        set { _groupId = value; OnPropertyChanged(); }
    }

    public decimal CurrentPrice
    {
        get => _currentPrice;
        set
        {
            _currentPrice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PriceText));
            OnPropertyChanged(nameof(HoldingProfit));
            OnPropertyChanged(nameof(HoldingProfitPercent));
            OnPropertyChanged(nameof(TodayHoldingProfit));
            OnPropertyChanged(nameof(MarketValue));
        }
    }

    public decimal ChangePercent
    {
        get => _changePercent;
        set { _changePercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUp)); }
    }

    public bool IsUp => _changePercent >= 0;

    public string PriceText => IsFund ? $"{_currentPrice:F4}" : $"{_currentPrice:F2}";

    public decimal YesterdayClose
    {
        get => _yesterdayClose;
        set { _yesterdayClose = value; OnPropertyChanged(); OnPropertyChanged(nameof(TodayHoldingProfit)); }
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

    public decimal TurnoverRate
    {
        get => _turnoverRate;
        set { _turnoverRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(TurnoverRateText)); }
    }

    public decimal VolumeRatio
    {
        get => _volumeRatio;
        set { _volumeRatio = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumeRatioText)); }
    }

    public decimal Speed
    {
        get => _speed;
        set { _speed = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); OnPropertyChanged(nameof(IsSpeedUp)); }
    }

    public decimal TotalMarketValue
    {
        get => _totalMarketValue;
        set { _totalMarketValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalMarketValueText)); }
    }

    public decimal FloatMarketValue
    {
        get => _floatMarketValue;
        set { _floatMarketValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(FloatMarketValueText)); }
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

    public string TurnoverRateText => _turnoverRate > 0 ? $"{_turnoverRate:F2}%" : "--";

    public string VolumeRatioText => _volumeRatio > 0 ? $"{_volumeRatio:F2}" : "--";

    public string SpeedText => _speed != 0 ? $"{_speed:+0.00;-0.00;0.00}%" : "--";

    public bool IsSpeedUp => _speed > 0;

    public string TotalMarketValueText => FormatMoney(_totalMarketValue);

    public string FloatMarketValueText => FormatMoney(_floatMarketValue);

    public decimal HoldingShares
    {
        get => _holdingShares;
        set
        {
            _holdingShares = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHolding));
            OnPropertyChanged(nameof(HoldingProfit));
            OnPropertyChanged(nameof(HoldingProfitPercent));
            OnPropertyChanged(nameof(TodayHoldingProfit));
            OnPropertyChanged(nameof(MarketValue));
        }
    }

    public decimal HoldingCost
    {
        get => _holdingCost;
        set
        {
            _holdingCost = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHolding));
            OnPropertyChanged(nameof(HoldingProfit));
            OnPropertyChanged(nameof(HoldingProfitPercent));
        }
    }

    public bool HasHolding => _holdingShares > 0 && _holdingCost > 0;

    public decimal HoldingProfit => HasHolding ? (_currentPrice - _holdingCost) * _holdingShares : 0;

    public decimal HoldingProfitPercent => HasHolding && _holdingCost > 0
        ? (_currentPrice - _holdingCost) / _holdingCost * 100m
        : 0;

    public decimal TodayHoldingProfit => HasHolding && _yesterdayClose > 0
        ? (_currentPrice - _yesterdayClose) * _holdingShares
        : 0;

    public decimal MarketValue => _currentPrice * _holdingShares;

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

    private static string FormatMoney(decimal value)
    {
        if (value <= 0) return "--";
        if (value >= 1_0000_0000) return $"{value / 1_0000_0000:F2}亿";
        if (value >= 1_0000) return $"{value / 1_0000:F2}万";
        return $"{value:F2}";
    }
}
