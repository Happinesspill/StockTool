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
    public const string DefaultGroupName = "自选";

    private readonly ConfigStore _configStore;
    private readonly QuoteService _quoteService;
    private readonly IntradayService _intradayService;
    private AppConfig _config;

    public ObservableCollection<StockItem> Stocks { get; } = [];
    public ObservableCollection<StockItem> VisibleStocks { get; } = [];
    public ObservableCollection<WatchlistGroup> Groups { get; } = [];
    public ObservableCollection<IndexItem> Indices { get; } = [];
    /// <summary>管理窗：全部可选指数及勾选状态。</summary>
    public ObservableCollection<IndexOptionItem> IndexOptions { get; } = [];
    /// <summary>管理窗：已选指数卡片（与 Indices 同源顺序）。</summary>
    public ObservableCollection<IndexItem> SelectedHomeIndices { get; } = [];
    public event Action<string>? AlertTriggered;
    public decimal HoldingMarketValue => Stocks.Where(s => s.HasHolding).Sum(s => s.MarketValue);
    public decimal HoldingProfitTotal => Stocks.Where(s => s.HasHolding).Sum(s => s.HoldingProfit);
    public decimal HoldingTodayProfit => Stocks.Where(s => s.HasHolding).Sum(s => s.TodayHoldingProfit);
    public string HoldingMarketValueText => FormatMoney(HoldingMarketValue);
    public string HoldingProfitTotalText => $"{HoldingProfitTotal:+0.##;-0.##;0.##}";
    public string HoldingTodayProfitText => $"{HoldingTodayProfit:+0.##;-0.##;0.##}";
    public int AlertCount => _config.AlertRules.Count(r => r.Enabled);

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

    public bool IsEditMode
    {
        get => _config.IsEditMode;
        set
        {
            if (_config.IsEditMode == value) return;
            _config.IsEditMode = value;
            _configStore.Save(_config);
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditModeText));
        }
    }

    public string EditModeText => IsEditMode ? "完成" : "编辑列表";

    public string SelectedGroupId
    {
        get => _config.SelectedGroupId;
        set
        {
            if (_config.SelectedGroupId == value) return;
            _config.SelectedGroupId = value;
            _configStore.Save(_config);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedGroupName));
            RefreshVisibleStocks();
        }
    }

    public string SelectedGroupName =>
        Groups.FirstOrDefault(g => g.Id == SelectedGroupId)?.Name ?? DefaultGroupName;

    public string SortMode
    {
        get => _config.SortMode;
        set
        {
            if (_config.SortMode == value) return;
            _config.SortMode = value;
            _config.SortColumn = value == "change" ? "change" : "default";
            _configStore.Save(_config);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSortByChange));
            OnPropertyChanged(nameof(SortArrowText));
            OnSortArrowsChanged();
            OnPropertyChanged(nameof(CanReorder));
            RefreshVisibleStocks();
        }
    }

    public string SortColumn
    {
        get => _config.SortColumn;
        set
        {
            if (_config.SortColumn == value) return;
            _config.SortColumn = value;
            _config.SortMode = value == "change" ? "change" : "default";
            _configStore.Save(_config);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSortByChange));
            OnPropertyChanged(nameof(SortArrowText));
            OnSortArrowsChanged();
            OnPropertyChanged(nameof(CanReorder));
            RefreshVisibleStocks();
        }
    }

    public bool SortDescending
    {
        get => _config.SortDescending;
        set
        {
            if (_config.SortDescending == value) return;
            _config.SortDescending = value;
            _configStore.Save(_config);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SortArrowText));
            OnSortArrowsChanged();
            RefreshVisibleStocks();
        }
    }

    public bool IsSortByChange => SortColumn == "change";
    public string SortArrowText => SortColumn == "default" ? "" : (SortDescending ? "▾" : "▴");
    public string ChangeSortArrow => GetSortArrow("change");
    public string ProfitSortArrow => GetSortArrow("profit");
    public string TurnoverSortArrow => GetSortArrow("turnover");
    public string TurnoverRateSortArrow => GetSortArrow("turnoverRate");
    public string SpeedSortArrow => GetSortArrow("speed");
    public string VolumeRatioSortArrow => GetSortArrow("volumeRatio");
    public string MarketValueSortArrow => GetSortArrow("marketValue");
    public bool CanReorder => SortColumn == "default";

    private StockItem? _selectedStock;
    public StockItem? SelectedStock
    {
        get => _selectedStock;
        set
        {
            _selectedStock = value;
            IsChartExpanded = value != null;
            OnPropertyChanged();
        }
    }

    private bool _isChartExpanded;
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

    public int ChartSubCount => _isChartExpanded ? 3 : 0;
    public string ExpandHintText => _isChartExpanded ? "∨  收起指标" : "⌃  查看详情";

    public MainViewModel(ConfigStore configStore, EastMoneyClient eastMoneyClient)
    {
        _configStore = configStore;
        _config = _configStore.Load();
        _config.EnsureGroupsMigrated();

        foreach (var g in _config.Groups)
            Groups.Add(g);

        _config.EnsureHomeIndicesMigrated();
        RebuildIndices();
        RebuildIndexOptions();

        _quoteService = new QuoteService(eastMoneyClient, Stocks, Indices, () =>
        {
            var markets = Stocks.Select(s => s.Market).Distinct();
            bool trading = TradingHours.AnyTrading(markets);
            return TradingHours.GetQuoteInterval(_config.RefreshInterval, trading);
        }, OnQuotesUpdated);

        _intradayService = new IntradayService(eastMoneyClient, Stocks);

        if (_config.Watchlist.Count == 0)
        {
            _config.Watchlist = GetDefaultWatchlist(_config.SelectedGroupId);
            _configStore.Save(_config);
        }

        foreach (var entry in _config.Watchlist)
        {
            Stocks.Add(new StockItem
            {
                Name = entry.Name,
                Code = entry.Code,
                Market = entry.Market,
                CustomName = entry.CustomName,
                GroupId = entry.GroupId,
                HoldingShares = entry.HoldingShares,
                HoldingCost = entry.HoldingCost
            });
        }

        RefreshVisibleStocks();

        _quoteService.Start();
        _ = _quoteService.RefreshNowAsync();
        _ = _intradayService.LoadAllAsync();
        _intradayService.Start();

        UpdateTimers();
    }

    private void OnQuotesUpdated()
    {
        EvaluateAlerts();
        OnPropertyChanged(nameof(HoldingMarketValueText));
        OnPropertyChanged(nameof(HoldingProfitTotalText));
        OnPropertyChanged(nameof(HoldingTodayProfitText));
        if (SortColumn != "default")
            RefreshVisibleStocks();
    }

    public void AddAlert(StockItem stock, AlertMetric metric, AlertDirection direction, decimal threshold)
    {
        _config.AlertRules.RemoveAll(r => r.StockCode.Equals(stock.Code, StringComparison.OrdinalIgnoreCase)
            && r.Metric == metric && r.Direction == direction);
        _config.AlertRules.Add(new AlertRule { StockCode = stock.Code, Metric = metric, Direction = direction, Threshold = threshold });
        _configStore.Save(_config);
        OnPropertyChanged(nameof(AlertCount));
    }

    public void RemoveAlert(AlertRule rule)
    {
        _config.AlertRules.Remove(rule);
        _configStore.Save(_config);
        OnPropertyChanged(nameof(AlertCount));
    }

    private void EvaluateAlerts()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        foreach (var rule in _config.AlertRules.Where(r => r.Enabled && r.LastTriggeredDate != today).ToList())
        {
            var stock = Stocks.FirstOrDefault(s => s.Code.Equals(rule.StockCode, StringComparison.OrdinalIgnoreCase));
            if (stock == null) continue;
            decimal value = rule.Metric == AlertMetric.Price ? stock.CurrentPrice : stock.ChangePercent;
            bool hit = rule.Direction == AlertDirection.Above ? value >= rule.Threshold : value <= rule.Threshold;
            if (!hit) continue;
            rule.LastTriggeredDate = today;
            _configStore.Save(_config);
            AlertTriggered?.Invoke($"{stock.DisplayName} {rule.Description}，当前 {value:0.##}");
        }
    }

    private static string FormatMoney(decimal value)
    {
        decimal abs = Math.Abs(value);
        return abs >= 100000000m ? $"{value / 100000000m:0.##}亿" : abs >= 10000m ? $"{value / 10000m:0.##}万" : $"{value:0.##}";
    }

    public void RebuildIndices()
    {
        _config.EnsureHomeIndicesMigrated();
        Indices.Clear();
        SelectedHomeIndices.Clear();
        foreach (var code in _config.HomeIndexCodes)
        {
            var preset = IndexCatalog.Find(code);
            if (preset == null) continue;
            var item = new IndexItem
            {
                Name = preset.Name,
                Code = preset.Code,
                DisplayCode = preset.DisplayCode
            };
            Indices.Add(item);
            SelectedHomeIndices.Add(item);
        }
    }

    public void RebuildIndexOptions()
    {
        IndexOptions.Clear();
        var selected = new HashSet<string>(_config.HomeIndexCodes, StringComparer.OrdinalIgnoreCase);
        foreach (var preset in IndexCatalog.All)
        {
            IndexOptions.Add(new IndexOptionItem
            {
                Name = preset.Name,
                DisplayCode = preset.DisplayCode,
                Code = preset.Code,
                IsSelected = selected.Contains(preset.Code)
            });
        }
    }

    /// <returns>null 成功；否则为失败原因。</returns>
    public string? ToggleHomeIndex(string code)
    {
        if (string.IsNullOrEmpty(code) || IndexCatalog.Find(code) == null)
            return "未知指数";

        int idx = _config.HomeIndexCodes.FindIndex(c =>
            string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            if (_config.HomeIndexCodes.Count <= 1)
                return "至少保留一个首页指数";
            _config.HomeIndexCodes.RemoveAt(idx);
        }
        else
        {
            if (_config.HomeIndexCodes.Count >= IndexCatalog.MaxHomeIndices)
                return $"首页最多选择 {IndexCatalog.MaxHomeIndices} 个指数";
            _config.HomeIndexCodes.Add(code);
        }

        _configStore.Save(_config);
        RebuildIndices();
        RebuildIndexOptions();
        _ = _quoteService.RefreshNowAsync();
        return null;
    }

    public string? RemoveHomeIndex(string code) => ToggleHomeIndex(code);

    public void MoveHomeIndex(string code, int offset)
    {
        int index = _config.HomeIndexCodes.FindIndex(c =>
            string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        int newIndex = index + offset;
        if (newIndex < 0 || newIndex >= _config.HomeIndexCodes.Count) return;

        (_config.HomeIndexCodes[index], _config.HomeIndexCodes[newIndex]) =
            (_config.HomeIndexCodes[newIndex], _config.HomeIndexCodes[index]);
        _configStore.Save(_config);
        RebuildIndices();
        RebuildIndexOptions();
        _ = _quoteService.RefreshNowAsync();
    }

    public void RefreshVisibleStocks()
    {
        var filtered = Stocks.Where(s => s.GroupId == SelectedGroupId).ToList();
        filtered = ApplySort(filtered).ToList();

        VisibleStocks.Clear();
        foreach (var s in filtered)
            VisibleStocks.Add(s);

        OnPropertyChanged(nameof(VisibleStocks));
    }

    public void SelectGroup(string groupId)
    {
        if (string.IsNullOrEmpty(groupId)) return;
        SelectedGroupId = groupId;
    }

    public void TogglePriceSort()
    {
        ToggleSort("change");
    }

    public void ToggleSort(string column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;

        if (SortColumn == column)
        {
            if (SortDescending)
            {
                SortDescending = false;
                return;
            }

            SortColumn = "default";
            SortDescending = true;
            return;
        }

        SortColumn = column;
        SortDescending = true;
    }

    public string GetSortArrow(string column)
    {
        if (!string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase))
            return "";
        return SortDescending ? "▾" : "▴";
    }

    private void OnSortArrowsChanged()
    {
        OnPropertyChanged(nameof(ChangeSortArrow));
        OnPropertyChanged(nameof(ProfitSortArrow));
        OnPropertyChanged(nameof(TurnoverSortArrow));
        OnPropertyChanged(nameof(TurnoverRateSortArrow));
        OnPropertyChanged(nameof(SpeedSortArrow));
        OnPropertyChanged(nameof(VolumeRatioSortArrow));
        OnPropertyChanged(nameof(MarketValueSortArrow));
    }

    public void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
    }

    public WatchlistGroup? AddGroup(string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        if (Groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            return null;

        var group = new WatchlistGroup { Id = Guid.NewGuid().ToString("N"), Name = name };
        Groups.Add(group);
        _config.Groups.Add(group);
        _configStore.Save(_config);
        SelectedGroupId = group.Id;
        return group;
    }

    public bool RenameGroup(string groupId, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName)) return false;
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;
        if (Groups.Any(g => g.Id != groupId && string.Equals(g.Name, newName, StringComparison.OrdinalIgnoreCase)))
            return false;

        group.Name = newName;
        _configStore.Save(_config);
        OnPropertyChanged(nameof(SelectedGroupName));
        return true;
    }

    // 拖拽调整分组顺序
    public void MoveGroup(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Groups.Count) return;
        if (toIndex < 0 || toIndex >= Groups.Count) return;
        if (fromIndex == toIndex) return;

        Groups.Move(fromIndex, toIndex);
        var item = _config.Groups[fromIndex];
        _config.Groups.RemoveAt(fromIndex);
        _config.Groups.Insert(toIndex, item);
        _configStore.Save(_config);
    }

    public bool DeleteGroup(string groupId)
    {
        if (Groups.Count <= 1) return false;
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;

        // 「自选」不可删
        if (group.Name == DefaultGroupName) return false;

        var fallback = Groups.FirstOrDefault(g => g.Name == DefaultGroupName) ?? Groups.First(g => g.Id != groupId);

        foreach (var stock in Stocks.Where(s => s.GroupId == groupId))
            stock.GroupId = fallback.Id;
        foreach (var entry in _config.Watchlist.Where(e => e.GroupId == groupId))
            entry.GroupId = fallback.Id;

        Groups.Remove(group);
        _config.Groups.RemoveAll(g => g.Id == groupId);

        if (SelectedGroupId == groupId)
            SelectedGroupId = fallback.Id;
        else
            RefreshVisibleStocks();

        _configStore.Save(_config);
        return true;
    }

    public void AddStock(string code, string name, string market)
    {
        string groupId = SelectedGroupId;
        if (string.IsNullOrEmpty(groupId) && Groups.Count > 0)
            groupId = Groups[0].Id;

        var entry = new WatchlistEntry
        {
            Code = code,
            Name = name,
            Market = market,
            GroupId = groupId
        };
        _config.Watchlist.Add(entry);
        _configStore.Save(_config);

        Stocks.Add(new StockItem
        {
            Name = name,
            Code = code,
            Market = market,
            GroupId = groupId
        });
        RefreshVisibleStocks();
        _ = _intradayService.LoadAllAsync();
    }

    public void RemoveStock(StockItem stock)
    {
        int index = Stocks.IndexOf(stock);
        if (index < 0) return;

        if (SelectedStock == stock)
            SelectedStock = null;

        Stocks.RemoveAt(index);
        int cfgIndex = FindWatchlistIndex(stock);
        if (cfgIndex >= 0)
            _config.Watchlist.RemoveAt(cfgIndex);
        else if (index < _config.Watchlist.Count)
            _config.Watchlist.RemoveAt(index);

        _configStore.Save(_config);
        RefreshVisibleStocks();
    }

    public void RenameStock(StockItem stock, string customName)
    {
        int index = Stocks.IndexOf(stock);
        if (index < 0) return;

        stock.CustomName = customName;
        int cfgIndex = FindWatchlistIndex(stock);
        if (cfgIndex >= 0)
            _config.Watchlist[cfgIndex].CustomName = customName;
        _configStore.Save(_config);
    }

    public void SetHolding(StockItem stock, decimal shares, decimal cost)
    {
        int index = Stocks.IndexOf(stock);
        if (index < 0) return;

        stock.HoldingShares = shares;
        stock.HoldingCost = cost;

        int cfgIndex = FindWatchlistIndex(stock);
        if (cfgIndex >= 0)
        {
            _config.Watchlist[cfgIndex].HoldingShares = shares;
            _config.Watchlist[cfgIndex].HoldingCost = cost;
            _configStore.Save(_config);
        }
    }

    public void MoveStockUp(StockItem stock)
    {
        if (!CanReorder) return;
        int visIndex = VisibleStocks.IndexOf(stock);
        if (visIndex <= 0) return;
        MoveVisibleStock(visIndex, visIndex - 1);
    }

    public void MoveStockDown(StockItem stock)
    {
        if (!CanReorder) return;
        int visIndex = VisibleStocks.IndexOf(stock);
        if (visIndex < 0 || visIndex >= VisibleStocks.Count - 1) return;
        MoveVisibleStock(visIndex, visIndex + 1);
    }

    public void MoveStockToTop(StockItem stock)
    {
        if (!CanReorder) return;
        int visIndex = VisibleStocks.IndexOf(stock);
        if (visIndex <= 0) return;
        MoveVisibleStock(visIndex, 0);
    }

    public void MoveStockToBottom(StockItem stock)
    {
        if (!CanReorder) return;
        int visIndex = VisibleStocks.IndexOf(stock);
        if (visIndex < 0 || visIndex >= VisibleStocks.Count - 1) return;
        MoveVisibleStock(visIndex, VisibleStocks.Count - 1);
    }

    public bool MoveStockToGroup(StockItem stock, string groupId)
    {
        if (string.IsNullOrEmpty(groupId) || Groups.All(g => g.Id != groupId)) return false;
        int index = Stocks.IndexOf(stock);
        if (index < 0 || stock.GroupId == groupId) return false;

        int cfgIndex = FindWatchlistIndex(stock);
        stock.GroupId = groupId;
        if (cfgIndex >= 0)
            _config.Watchlist[cfgIndex].GroupId = groupId;

        _configStore.Save(_config);
        RefreshVisibleStocks();
        return true;
    }

    public bool RemoveStockFromCurrentGroup(StockItem stock)
    {
        if (Groups.Count <= 1) return false;
        var target = Groups.FirstOrDefault(g => g.Id != stock.GroupId);
        if (target == null) return false;
        return MoveStockToGroup(stock, target.Id);
    }

    public void MoveVisibleStock(int fromVisibleIndex, int toVisibleIndex)
    {
        if (!CanReorder) return;
        if (fromVisibleIndex < 0 || fromVisibleIndex >= VisibleStocks.Count) return;
        if (toVisibleIndex < 0 || toVisibleIndex >= VisibleStocks.Count) return;
        if (fromVisibleIndex == toVisibleIndex) return;

        var dragged = VisibleStocks[fromVisibleIndex];
        var target = VisibleStocks[toVisibleIndex];

        int from = Stocks.IndexOf(dragged);
        int to = Stocks.IndexOf(target);
        if (from < 0 || to < 0) return;

        Stocks.Move(from, to);
        var item = _config.Watchlist[from];
        _config.Watchlist.RemoveAt(from);
        _config.Watchlist.Insert(to, item);
        _configStore.Save(_config);
        RefreshVisibleStocks();
    }

    private int FindWatchlistIndex(StockItem stock)
    {
        int index = Stocks.IndexOf(stock);
        if (index >= 0 && index < _config.Watchlist.Count && ReferenceMatches(_config.Watchlist[index], stock))
            return index;

        return _config.Watchlist.FindIndex(e => ReferenceMatches(e, stock));
    }

    private static bool ReferenceMatches(WatchlistEntry entry, StockItem stock) =>
        string.Equals(entry.Code, stock.Code, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.GroupId, stock.GroupId, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<StockItem> ApplySort(IEnumerable<StockItem> stocks)
    {
        Func<StockItem, decimal> selector = SortColumn switch
        {
            "price" => s => s.CurrentPrice,
            "change" => s => s.ChangePercent,
            "profit" => s => s.HoldingProfit,
            "turnover" => s => s.Turnover,
            "turnoverRate" => s => s.TurnoverRate,
            "speed" => s => s.Speed,
            "volumeRatio" => s => s.VolumeRatio,
            "marketValue" => s => s.FloatMarketValue > 0 ? s.FloatMarketValue : s.TotalMarketValue,
            _ => s => 0
        };

        if (SortColumn == "default")
            return stocks;

        return SortDescending
            ? stocks.OrderByDescending(selector).ThenBy(s => s.DisplayName)
            : stocks.OrderBy(selector).ThenBy(s => s.DisplayName);
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

        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(RefreshInterval));
        OnPropertyChanged(nameof(Hotkey));
        OnPropertyChanged(nameof(Topmost));
        OnPropertyChanged(nameof(ShowMarketTag));

        UpdateTimers();
    }

    public void UpdateTimers()
    {
        var markets = Stocks.Select(s => s.Market).Distinct();
        bool trading = TradingHours.AnyTrading(markets);
        _quoteService.UpdateInterval();
        _intradayService.SetInterval(TradingHours.GetIntradayInterval(trading));
    }

    private static List<WatchlistEntry> GetDefaultWatchlist(string groupId) =>
    [
        new() { Name = "招商银行", Code = "SH600036", Market = "沪A", GroupId = groupId },
        new() { Name = "中国平安", Code = "SH601318", Market = "沪A", GroupId = groupId },
        new() { Name = "小米集团", Code = "HK01810", Market = "港股", GroupId = groupId },
        new() { Name = "新华保险", Code = "SH601336", Market = "沪A", GroupId = groupId },
        new() { Name = "海尔智家", Code = "SH600690", Market = "沪A", GroupId = groupId },
        new() { Name = "沪深300ETF", Code = "SH510300", Market = "ETF", GroupId = groupId },
        new() { Name = "黄金股ETF", Code = "SH159562", Market = "ETF", GroupId = groupId },
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
