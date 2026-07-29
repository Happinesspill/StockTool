using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StockTool.Core.Models;
using StockTool.Core.Services;
using StockTool.Data;
using StockTool.Services;

namespace StockTool.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ConfigStore _configStore;
    private readonly QuoteService _quoteService;
    private readonly IntradayService _intradayService;
    private AppConfig _config;

    public ObservableCollection<StockItem> Stocks { get; } = [];

    public double WindowLeft
    {
        get => _config.WindowLeft;
        set { _config.WindowLeft = value; OnPropertyChanged(); }
    }

    public double WindowTop
    {
        get => _config.WindowTop;
        set { _config.WindowTop = value; OnPropertyChanged(); }
    }

    public double WindowWidth
    {
        get => _config.WindowWidth;
        set { _config.WindowWidth = value; OnPropertyChanged(); }
    }

    public double WindowHeight
    {
        get => _config.WindowHeight;
        set { _config.WindowHeight = value; OnPropertyChanged(); }
    }

    public double Opacity
    {
        get => _config.Opacity;
        set { _config.Opacity = value; OnPropertyChanged(); }
    }

    public int RefreshInterval
    {
        get => _config.RefreshInterval;
        set { _config.RefreshInterval = value; OnPropertyChanged(); }
    }

    public bool Topmost
    {
        get => _config.Topmost;
        set { _config.Topmost = value; OnPropertyChanged(); }
    }

    public int FontSize
    {
        get => _config.FontSize;
        set { _config.FontSize = value; OnPropertyChanged(); }
    }

    public string Hotkey
    {
        get => _config.Hotkey;
        set { _config.Hotkey = value; OnPropertyChanged(); }
    }

    public bool ShowMarketTag
    {
        get => _config.ShowMarketTag;
        set { _config.ShowMarketTag = value; OnPropertyChanged(); }
    }

    private StockItem? _selectedStock;
    public StockItem? SelectedStock
    {
        get => _selectedStock;
        set
        {
            _selectedStock = value;
            // 打开详情时直接展开 MACD/KDJ/RSI
            IsChartExpanded = value != null;
            OnPropertyChanged();
        }
    }

    private bool _isChartExpanded;
    /// <summary>是否展开 MACD/KDJ/RSI 副图</summary>
    public bool IsChartExpanded
    {
        get => _isChartExpanded;
        set
        {
            if (_isChartExpanded == value) return;
            _isChartExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChartSubCount));
            OnPropertyChanged(nameof(ExpandHintText));
        }
    }

    /// <summary>传给 K 线控件：展开时显示 3 个副图，否则仅主图</summary>
    public int ChartSubCount => _isChartExpanded ? 3 : 0;

    public string ExpandHintText => _isChartExpanded ? "∨  收起指标" : "⌃  查看详情";

    public MainViewModel(ConfigStore configStore, EastMoneyClient eastMoneyClient)
    {
        _configStore = configStore;
        _config = _configStore.Load();

        _quoteService = new QuoteService(eastMoneyClient, Stocks, () =>
        {
            var markets = Stocks.Select(s => s.Market).Distinct();
            bool trading = TradingHours.AnyTrading(markets);
            return TradingHours.GetQuoteInterval(_config.RefreshInterval, trading);
        });

        _intradayService = new IntradayService(eastMoneyClient, Stocks);

        if (_config.Watchlist.Count == 0)
        {
            _config.Watchlist = GetDefaultWatchlist();
            _configStore.Save(_config);
        }

        foreach (var entry in _config.Watchlist)
        {
            Stocks.Add(new StockItem
            {
                Name = entry.Name,
                Code = entry.Code,
                Market = entry.Market,
                CustomName = entry.CustomName
            });
        }

        _quoteService.Start();
        _ = _quoteService.RefreshNowAsync();
        _ = _intradayService.LoadAllAsync();
        _intradayService.Start();

        UpdateTimers();
    }

    public void AddStock(string code, string name, string market)
    {
        var entry = new WatchlistEntry { Code = code, Name = name, Market = market };
        _config.Watchlist.Add(entry);
        _configStore.Save(_config);

        Stocks.Add(new StockItem { Name = name, Code = code, Market = market });
        _ = _intradayService.LoadAllAsync();
    }

    public void RemoveStock(int index)
    {
        if (index < 0 || index >= Stocks.Count)
            return;

        var removed = Stocks[index];
        if (SelectedStock == removed)
            SelectedStock = null;

        Stocks.RemoveAt(index);
        _config.Watchlist.RemoveAt(index);
        _configStore.Save(_config);
    }

    public void RenameStock(int index, string customName)
    {
        if (index < 0 || index >= Stocks.Count)
            return;

        Stocks[index].CustomName = customName;
        _config.Watchlist[index].CustomName = customName;
        _configStore.Save(_config);
    }

    public void MoveStockUp(int index)
    {
        MoveStock(index, index - 1);
    }

    public void MoveStockDown(int index)
    {
        MoveStock(index, index + 1);
    }

    public void MoveStock(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Stocks.Count) return;
        if (toIndex < 0 || toIndex >= Stocks.Count) return;
        if (fromIndex == toIndex) return;

        Stocks.Move(fromIndex, toIndex);

        // Swap in config watchlist to match
        var item = _config.Watchlist[fromIndex];
        _config.Watchlist.RemoveAt(fromIndex);
        _config.Watchlist.Insert(toIndex, item);
        _configStore.Save(_config);
    }

    public void SaveWindowState(double left, double top, double width, double height)
    {
        _config.WindowLeft = left;
        _config.WindowTop = top;
        _config.WindowWidth = width;
        _config.WindowHeight = height;
        _configStore.Save(_config);
    }

    public void SaveSettings(double opacity, int fontSize, int refreshInterval, string hotkey, bool topmost, bool showMarketTag)
    {
        _config.Opacity = opacity;
        _config.FontSize = fontSize;
        _config.RefreshInterval = refreshInterval;
        _config.Hotkey = hotkey;
        _config.Topmost = topmost;
        _config.ShowMarketTag = showMarketTag;
        _configStore.Save(_config);

        // notify bindings
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(RefreshInterval));
        OnPropertyChanged(nameof(Hotkey));
        OnPropertyChanged(nameof(Topmost));
        OnPropertyChanged(nameof(ShowMarketTag));

        // sync timer intervals
        UpdateTimers();
    }

    public void UpdateTimers()
    {
        var markets = Stocks.Select(s => s.Market).Distinct();
        bool trading = TradingHours.AnyTrading(markets);
        _quoteService.UpdateInterval();
        _intradayService.SetInterval(TradingHours.GetIntradayInterval(trading));
    }

    private static List<WatchlistEntry> GetDefaultWatchlist() => new()
    {
        new() { Name = "招商银行",   Code = "SH600036", Market = "沪A" },
        new() { Name = "中国平安",   Code = "SH601318", Market = "沪A" },
        new() { Name = "小米集团",   Code = "HK01810", Market = "港股" },
        new() { Name = "新华保险",   Code = "SH601336", Market = "沪A" },
        new() { Name = "海尔智家",   Code = "SH600690", Market = "沪A" },
        new() { Name = "沪深300ETF", Code = "SH510300", Market = "ETF" },
        new() { Name = "黄金股ETF",  Code = "SH159562", Market = "ETF" },
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
