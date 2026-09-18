using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using StockTool.Core.Models;
using StockTool.Core.Services;
using StockTool.Data;
using StockTool.Services;

namespace StockTool.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public const string DefaultGroupName = "自选";
    public const string FundGroupName = "基金";
    public const string HoldingGroupName = WatchlistGroup.HoldingGroupName;
    public const string HoldingGroupId = WatchlistGroup.HoldingGroupId;
    public const int FixedGroupCount = 2;

    private readonly ConfigStore _configStore;
    private readonly QuoteService _quoteService;
    private readonly IntradayService _intradayService;
    private readonly FundNavService _fundNavService;
    private readonly FundDcaService _fundDcaService;
    private AppConfig _config;
    private FundPortfolioConfig _funds;

    public ObservableCollection<StockItem> Stocks { get; } = [];
    public ObservableCollection<StockItem> VisibleStocks { get; } = [];
    public ObservableCollection<WatchlistGroup> Groups { get; } = [];
    public ObservableCollection<IndexItem> Indices { get; } = [];
    /// <summary>管理窗：全部可选指数及勾选状态。</summary>
    public ObservableCollection<IndexOptionItem> IndexOptions { get; } = [];
    /// <summary>管理窗：已选指数卡片（与 Indices 同源顺序）。</summary>
    public ObservableCollection<IndexItem> SelectedHomeIndices { get; } = [];
    public event Action<string>? AlertTriggered;
    public decimal HoldingMarketValue => IsFundGroup
        ? GetDistinctFundHoldings().Sum(s => s.HoldingAmount)
        : GetDistinctStockHoldings().Sum(s => s.MarketValue);
    public decimal HoldingProfitTotal => IsFundGroup
        ? GetDistinctFundHoldings().Sum(s => s.HoldingProfit)
        : GetDistinctStockHoldings().Sum(s => s.HoldingProfit);
    public decimal HoldingTodayProfit => IsFundGroup
        ? GetDistinctFundHoldings().Sum(s => s.TodayHoldingProfit)
        : GetDistinctStockHoldings().Sum(s => s.TodayHoldingProfit);
    public string HoldingMarketValueLabel => IsFundGroup ? "基金总持仓" : "持仓市值";
    public string HoldingMarketValueText => FormatMoney(HoldingMarketValue);
    public string HoldingProfitTotalText => $"{HoldingProfitTotal:+0.##;-0.##;0.##}";
    public string HoldingTodayProfitText => $"{HoldingTodayProfit:+0.##;-0.##;0.##}";

    // 今日盈亏悬浮明细
    public IReadOnlyList<HoldingTooltipItem> HoldingTodayProfitDetails
    {
        get
        {
            var holdings = (IsFundGroup ? GetDistinctFundHoldings() : GetDistinctStockHoldings()).ToList();
            if (holdings.Count == 0)
                return [new HoldingTooltipItem("暂无持仓", "")];

            return holdings.Select(s => new HoldingTooltipItem(
                s.DisplayName,
                s.IsFund && !s.HasFundValuation
                    ? "--"
                    : $"{s.TodayHoldingProfit:+0.##;-0.##;0.##}  {s.ChangePercentText}")).ToList();
        }
    }

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

    public string HoldingVisionProvider =>
        string.IsNullOrWhiteSpace(_config.HoldingVisionProvider) ? "deepseek" : _config.HoldingVisionProvider;

    public string DeepSeekApiKey => _config.DeepSeekApiKey ?? "";
    public string MimoApiKey => _config.MimoApiKey ?? "";

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
            OnPropertyChanged(nameof(ShowHoldingColumn));
            OnPropertyChanged(nameof(ShowHoldingAmountColumn));
            OnPropertyChanged(nameof(ShowHoldingSharesColumn));
            OnPropertyChanged(nameof(ShowHoldingSummary));
            OnPropertyChanged(nameof(ShowHoldingSummaryDetails));
            OnPropertyChanged(nameof(IsHoldingGroupSelected));
            OnPropertyChanged(nameof(ShowTrendColumn));
            NotifyColumnWidthsChanged();
            OnPropertyChanged(nameof(IsFundGroup));
            OnPropertyChanged(nameof(EmptyListHint));
            OnPropertyChanged(nameof(PriceColumnHeader));
            OnPropertyChanged(nameof(NameColumnHeader));
            OnPropertyChanged(nameof(HoldingProfitColumnHeader));
            OnPropertyChanged(nameof(HoldingMarketValueLabel));
            OnPropertyChanged(nameof(HoldingMarketValueText));
            OnPropertyChanged(nameof(HoldingProfitTotalText));
            OnPropertyChanged(nameof(HoldingTodayProfitText));
            OnPropertyChanged(nameof(HoldingTodayProfitDetails));
            OnPropertyChanged(nameof(StatusSourceText));
            if (!ShowHoldingColumn && string.Equals(SortColumn, "profit", StringComparison.OrdinalIgnoreCase))
            {
                SortColumn = "default";
                SortDescending = true;
            }
            if (!ShowHoldingAmountColumn && string.Equals(SortColumn, "holdingAmount", StringComparison.OrdinalIgnoreCase))
            {
                SortColumn = "default";
                SortDescending = true;
            }
            if (!ShowHoldingSharesColumn && string.Equals(SortColumn, "holdingShares", StringComparison.OrdinalIgnoreCase))
            {
                SortColumn = "default";
                SortDescending = true;
            }
            RefreshVisibleStocks();
        }
    }

    public string SelectedGroupName =>
        Groups.FirstOrDefault(g => g.Id == SelectedGroupId)?.Name ?? DefaultGroupName;

    public bool ShowHoldingColumn => SelectedGroupId == HoldingGroupId || IsFundGroup;

    // 基金列表：名称后显示持有金额（份额×最新净值）
    public bool ShowHoldingAmountColumn => IsFundGroup;

    public bool ShowHoldingSharesColumn => IsHoldingGroupSelected;

    // 持仓/基金页显示汇总栏；其他分组不显示
    public bool ShowHoldingSummary => SelectedGroupId == HoldingGroupId || IsFundGroup;
    public bool ShowHoldingSummaryDetails => SelectedGroupId == HoldingGroupId || IsFundGroup;
    public bool IsHoldingGroupSelected => SelectedGroupId == HoldingGroupId;

    // 基金无可用盘中估值走势数据时不展示趋势列
    public bool ShowTrendColumn => !IsFundGroup;

    public GridLength NameColumnWidth => new(_config.NameColWidth);

    public GridLength HoldingAmountColumnWidth =>
        ShowHoldingAmountColumn ? new GridLength(_config.HoldingAmountColWidth) : new GridLength(0);

    public GridLength HoldingProfitColumnWidth =>
        ShowHoldingColumn ? new GridLength(_config.HoldingProfitColWidth) : new GridLength(0);

    public GridLength HoldingSharesColumnWidth =>
        ShowHoldingSharesColumn ? new GridLength(_config.HoldingSharesColWidth) : new GridLength(0);

    public GridLength TrendColumnWidth =>
        ShowTrendColumn ? new GridLength(_config.TrendColWidth) : new GridLength(0);

    public bool IsFundGroup => SelectedGroupName == FundGroupName;

    public string EmptyListHint => IsFundGroup ? "当前分组暂无基金" : "当前分组暂无股票";

    public GridLength PriceColumnWidth => new(_config.PriceColWidth);

    // 可见列宽合计，表头与列表同宽
    public double ListContentWidth =>
        _config.NameColWidth
        + (ShowHoldingAmountColumn ? _config.HoldingAmountColWidth : 0)
        + (ShowHoldingColumn ? _config.HoldingProfitColWidth : 0)
        + (ShowHoldingSharesColumn ? _config.HoldingSharesColWidth : 0)
        + (ShowTrendColumn ? _config.TrendColWidth : 0)
        + _config.PriceColWidth;

    // 拖拽右侧边框，仅调整当前列宽
    public void AdjustColumnWidth(string column, double delta)
    {
        switch (column)
        {
            case "name":
                _config.NameColWidth = Math.Max(0, _config.NameColWidth + delta);
                OnPropertyChanged(nameof(NameColumnWidth));
                break;
            case "holdingAmount":
                if (!ShowHoldingAmountColumn) return;
                _config.HoldingAmountColWidth = Math.Max(0, _config.HoldingAmountColWidth + delta);
                OnPropertyChanged(nameof(HoldingAmountColumnWidth));
                break;
            case "profit":
                if (!ShowHoldingColumn) return;
                _config.HoldingProfitColWidth = Math.Max(0, _config.HoldingProfitColWidth + delta);
                OnPropertyChanged(nameof(HoldingProfitColumnWidth));
                break;
            case "holdingShares":
                if (!ShowHoldingSharesColumn) return;
                _config.HoldingSharesColWidth = Math.Max(0, _config.HoldingSharesColWidth + delta);
                OnPropertyChanged(nameof(HoldingSharesColumnWidth));
                break;
            case "trend":
                if (!ShowTrendColumn) return;
                _config.TrendColWidth = Math.Max(0, _config.TrendColWidth + delta);
                OnPropertyChanged(nameof(TrendColumnWidth));
                break;
            case "price":
                _config.PriceColWidth = Math.Max(0, _config.PriceColWidth + delta);
                OnPropertyChanged(nameof(PriceColumnWidth));
                break;
            default:
                return;
        }
        OnPropertyChanged(nameof(ListContentWidth));
    }

    // 拖拽结束后持久化列宽
    public void SaveColumnWidths() => _configStore.Save(_config);

    private void NotifyColumnWidthsChanged()
    {
        OnPropertyChanged(nameof(NameColumnWidth));
        OnPropertyChanged(nameof(HoldingAmountColumnWidth));
        OnPropertyChanged(nameof(HoldingProfitColumnWidth));
        OnPropertyChanged(nameof(HoldingSharesColumnWidth));
        OnPropertyChanged(nameof(TrendColumnWidth));
        OnPropertyChanged(nameof(PriceColumnWidth));
        OnPropertyChanged(nameof(ListContentWidth));
    }

    public string PriceColumnHeader => IsFundGroup ? "预估盈利/涨幅" : "涨幅";

    public string NameColumnHeader => IsHoldingGroupSelected ? "市值" : "名称";

    public string HoldingProfitColumnHeader => IsFundGroup ? "持有收益" : "盈亏";

    public string StatusSourceText => IsFundGroup ? "数据来源：天天基金 | 仅供参考" : "数据来源：东方财富 | 仅供参考";

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
    public string HoldingAmountSortArrow => GetSortArrow("holdingAmount");
    public string HoldingSharesSortArrow => GetSortArrow("holdingShares");
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
        _funds = _configStore.MigrateFundsFromConfig(_config);
        RebuildGroupsCollection();

        _config.EnsureHomeIndicesMigrated();
        RebuildIndices();
        RebuildIndexOptions();

        _fundNavService = new FundNavService(eastMoneyClient);
        _fundDcaService = new FundDcaService(_configStore, _fundNavService, _funds);
        _fundDcaService.SetHoldingsChangedHandler(SyncFundHoldingsFromConfig);

        _quoteService = new QuoteService(eastMoneyClient, Stocks, Indices, () =>
        {
            var markets = Stocks.Select(s => s.Market).Distinct();
            bool trading = TradingHours.AnyTrading(markets);
            return TradingHours.GetQuoteInterval(_config.RefreshInterval, trading);
        }, OnQuotesUpdated);

        _intradayService = new IntradayService(eastMoneyClient, Stocks);

        if (_config.Watchlist.Count == 0 && _funds.Items.Count == 0)
        {
            _config.Watchlist = GetDefaultWatchlist(_config.SelectedGroupId == HoldingGroupId
                ? Groups.First(g => g.Name == DefaultGroupName).Id
                : _config.SelectedGroupId);
            _configStore.Save(_config);
        }

        foreach (var entry in _config.Watchlist)
            Stocks.Add(ToStockItem(entry));
        foreach (var entry in _funds.Items)
            Stocks.Add(ToStockItem(entry));

        RefreshVisibleStocks();

        _quoteService.Start();
        _ = _quoteService.RefreshNowAsync();
        _ = _intradayService.LoadAllAsync();
        _intradayService.Start();
        _fundDcaService.Start();

        UpdateTimers();
    }

    public FundNavService FundNav => _fundNavService;

    public IReadOnlyList<TradeRecord> GetFundTrades(string fundCode) =>
        _funds.Trades
            .Where(t => string.Equals(t.FundCode, fundCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Date)
            .ToList();

    public DcaPlan? GetDcaPlan(string fundCode) =>
        _funds.DcaPlans.FirstOrDefault(p =>
            string.Equals(p.FundCode, fundCode, StringComparison.OrdinalIgnoreCase));

    public void SetDcaPlan(string fundCode, decimal amount, bool enabled, DateTime? startDate = null)
    {
        var plan = GetDcaPlan(fundCode);
        if (plan == null)
        {
            plan = new DcaPlan
            {
                FundCode = fundCode,
                StartDate = (startDate ?? DateTime.Today).ToString("yyyy-MM-dd")
            };
            _funds.DcaPlans.Add(plan);
        }

        plan.Amount = amount;
        plan.Enabled = enabled && amount > 0;
        if (startDate.HasValue)
            plan.StartDate = startDate.Value.ToString("yyyy-MM-dd");
        if (string.IsNullOrEmpty(plan.StartDate))
            plan.StartDate = DateTime.Today.ToString("yyyy-MM-dd");

        _configStore.SaveFunds(_funds);
        _ = _fundDcaService.CatchUpAsync();
    }

    private static StockItem ToStockItem(WatchlistEntry entry) => new()
    {
        Name = entry.Name,
        Code = entry.Code,
        Market = entry.Market,
        CustomName = entry.CustomName,
        GroupId = entry.GroupId,
        HoldingShares = entry.HoldingShares,
        HoldingCost = entry.HoldingCost
    };

    private void SyncFundHoldingsFromConfig()
    {
        foreach (var entry in _funds.Items)
        {
            var stock = Stocks.FirstOrDefault(s =>
                string.Equals(s.Code, entry.Code, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.GroupId, entry.GroupId, StringComparison.OrdinalIgnoreCase));
            if (stock == null) continue;
            stock.HoldingShares = entry.HoldingShares;
            stock.HoldingCost = entry.HoldingCost;
        }

        OnPropertyChanged(nameof(HoldingMarketValueText));
        OnPropertyChanged(nameof(HoldingProfitTotalText));
        OnPropertyChanged(nameof(HoldingTodayProfitText));
        OnPropertyChanged(nameof(HoldingTodayProfitDetails));
        RefreshVisibleStocks();
    }

    private void SaveFunds()
    {
        _fundDcaService.SetFunds(_funds);
        _configStore.SaveFunds(_funds);
    }

    private bool IsFundStock(StockItem stock) => stock.IsFund;

    // 构建展示用分组：自选、持仓（虚拟）、其余
    private void RebuildGroupsCollection()
    {
        Groups.Clear();
        var zixuan = _config.Groups.FirstOrDefault(g => g.Name == DefaultGroupName) ?? _config.Groups[0];
        int zixuanIndex = _config.Groups.IndexOf(zixuan);
        if (zixuanIndex > 0)
        {
            _config.Groups.RemoveAt(zixuanIndex);
            _config.Groups.Insert(0, zixuan);
        }

        Groups.Add(zixuan);
        Groups.Add(new WatchlistGroup { Id = HoldingGroupId, Name = HoldingGroupName });
        foreach (var g in _config.Groups)
        {
            if (g.Id == zixuan.Id) continue;
            Groups.Add(g);
        }
    }

    private ManageGroupsSnapshot? _manageSnapshot;

    // 打开管理页时记录快照，供取消回滚
    public void BeginManageGroupsEdit()
    {
        _manageSnapshot = new ManageGroupsSnapshot
        {
            HomeIndexCodes = _config.HomeIndexCodes.ToList(),
            Groups = _config.Groups.Select(g => new WatchlistGroup { Id = g.Id, Name = g.Name }).ToList(),
            WatchlistGroupIds = _config.Watchlist.Select(e => e.GroupId).ToList(),
            SelectedGroupId = _config.SelectedGroupId
        };
    }

    public void CommitManageGroupsEdit()
    {
        _manageSnapshot = null;
        _configStore.Save(_config);
    }

    public void CancelManageGroupsEdit()
    {
        if (_manageSnapshot == null) return;
        var snap = _manageSnapshot;
        _manageSnapshot = null;

        _config.HomeIndexCodes = snap.HomeIndexCodes.ToList();
        _config.Groups = snap.Groups
            .Select(g => new WatchlistGroup { Id = g.Id, Name = g.Name })
            .ToList();

        int count = Math.Min(_config.Watchlist.Count, snap.WatchlistGroupIds.Count);
        for (int i = 0; i < count; i++)
        {
            _config.Watchlist[i].GroupId = snap.WatchlistGroupIds[i];
            if (i < Stocks.Count)
                Stocks[i].GroupId = snap.WatchlistGroupIds[i];
        }

        RebuildGroupsCollection();

        string selected = snap.SelectedGroupId;
        if (selected != HoldingGroupId && _config.Groups.All(g => g.Id != selected))
            selected = _config.Groups[0].Id;
        _config.SelectedGroupId = selected;

        RebuildIndices();
        RebuildIndexOptions();
        RefreshVisibleStocks();
        OnPropertyChanged(nameof(SelectedGroupId));
        OnPropertyChanged(nameof(SelectedGroupName));
        OnPropertyChanged(nameof(ShowHoldingColumn));
        OnPropertyChanged(nameof(ShowHoldingAmountColumn));
        OnPropertyChanged(nameof(ShowHoldingSharesColumn));
        OnPropertyChanged(nameof(ShowHoldingSummary));
        OnPropertyChanged(nameof(ShowHoldingSummaryDetails));
        OnPropertyChanged(nameof(IsHoldingGroupSelected));
        OnPropertyChanged(nameof(ShowTrendColumn));
        NotifyColumnWidthsChanged();
        OnPropertyChanged(nameof(IsFundGroup));
        OnPropertyChanged(nameof(EmptyListHint));
        OnPropertyChanged(nameof(PriceColumnHeader));
        OnPropertyChanged(nameof(NameColumnHeader));
        OnPropertyChanged(nameof(HoldingProfitColumnHeader));
        OnPropertyChanged(nameof(HoldingMarketValueLabel));
        OnPropertyChanged(nameof(HoldingMarketValueText));
        OnPropertyChanged(nameof(HoldingProfitTotalText));
        OnPropertyChanged(nameof(HoldingTodayProfitText));
        OnPropertyChanged(nameof(HoldingTodayProfitDetails));
        OnPropertyChanged(nameof(StatusSourceText));
        _configStore.Save(_config);
        _ = _quoteService.RefreshNowAsync();
    }

    private sealed class ManageGroupsSnapshot
    {
        public List<string> HomeIndexCodes { get; init; } = [];
        public List<WatchlistGroup> Groups { get; init; } = [];
        public List<string> WatchlistGroupIds { get; init; } = [];
        public string SelectedGroupId { get; init; } = string.Empty;
    }

    private void OnQuotesUpdated()
    {
        EvaluateAlerts();
        OnPropertyChanged(nameof(HoldingMarketValueText));
        OnPropertyChanged(nameof(HoldingProfitTotalText));
        OnPropertyChanged(nameof(HoldingTodayProfitText));
        OnPropertyChanged(nameof(HoldingTodayProfitDetails));
        if (SelectedGroupId == HoldingGroupId || SortColumn != "default")
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

    // 拖拽调整首页指数顺序
    public void ReorderHomeIndex(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _config.HomeIndexCodes.Count) return;
        if (toIndex < 0 || toIndex >= _config.HomeIndexCodes.Count) return;
        if (fromIndex == toIndex) return;

        var code = _config.HomeIndexCodes[fromIndex];
        _config.HomeIndexCodes.RemoveAt(fromIndex);
        _config.HomeIndexCodes.Insert(toIndex, code);
        _configStore.Save(_config);
        RebuildIndices();
        RebuildIndexOptions();
        _ = _quoteService.RefreshNowAsync();
    }

    public void RefreshVisibleStocks()
    {
        List<StockItem> filtered;
        if (SelectedGroupId == HoldingGroupId)
        {
            ConsolidateDuplicateStockHoldings();
            // 持仓页按代码去重，再按持仓市值从高到低
            filtered = GetDistinctStockHoldings()
                .OrderByDescending(s => s.MarketValue)
                .ThenBy(s => s.DisplayName)
                .ToList();
        }
        else
        {
            filtered = Stocks.Where(s => s.GroupId == SelectedGroupId).ToList();
            filtered = ApplySort(filtered).ToList();
        }

        VisibleStocks.Clear();
        foreach (var s in filtered)
            VisibleStocks.Add(s);

        OnPropertyChanged(nameof(VisibleStocks));
    }

    // 股票持仓按代码去重（同代码多分组只保留一条）
    private IEnumerable<StockItem> GetDistinctStockHoldings()
        => Stocks.Where(s => s.HasHolding && !s.IsFund)
            .GroupBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.MarketValue).ThenBy(s => s.DisplayName).First());

    private IEnumerable<StockItem> GetDistinctFundHoldings()
        => Stocks.Where(s => s.IsFund && s.HasHolding)
            .GroupBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.HoldingAmount).ThenBy(s => s.DisplayName).First());

    // 同代码多条持仓时只保留市值最高的一条
    private void ConsolidateDuplicateStockHoldings()
    {
        var dupGroups = Stocks
            .Where(s => s.HasHolding && !s.IsFund)
            .GroupBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        if (dupGroups.Count == 0) return;

        bool changed = false;
        foreach (var g in dupGroups)
        {
            var keep = g.OrderByDescending(s => s.MarketValue).ThenBy(s => s.DisplayName).First();
            foreach (var s in g)
            {
                if (ReferenceEquals(s, keep)) continue;
                s.HoldingShares = 0;
                s.HoldingCost = 0;
                int cfgIndex = FindWatchlistIndex(s);
                if (cfgIndex >= 0)
                {
                    _config.Watchlist[cfgIndex].HoldingShares = 0;
                    _config.Watchlist[cfgIndex].HoldingCost = 0;
                    changed = true;
                }
            }
        }

        if (changed)
            _configStore.Save(_config);
    }

    public static bool IsFixedGroup(WatchlistGroup group) =>
        group.Id == HoldingGroupId || group.Name == DefaultGroupName;

    public static bool IsHoldingGroup(string groupId) =>
        groupId == HoldingGroupId;

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
        OnPropertyChanged(nameof(HoldingAmountSortArrow));
        OnPropertyChanged(nameof(HoldingSharesSortArrow));
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
        if (string.Equals(name, HoldingGroupName, StringComparison.OrdinalIgnoreCase)) return null;
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
        if (groupId == HoldingGroupId) return false;
        if (string.Equals(newName, HoldingGroupName, StringComparison.OrdinalIgnoreCase)) return false;
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;
        if (Groups.Any(g => g.Id != groupId && string.Equals(g.Name, newName, StringComparison.OrdinalIgnoreCase)))
            return false;

        group.Name = newName;
        _configStore.Save(_config);
        OnPropertyChanged(nameof(SelectedGroupName));
        return true;
    }

    // 拖拽调整分组顺序（自选、持仓固定前两位）
    public void MoveGroup(int fromIndex, int toIndex)
    {
        if (fromIndex < FixedGroupCount || toIndex < FixedGroupCount) return;
        if (fromIndex >= Groups.Count || toIndex >= Groups.Count) return;
        if (fromIndex == toIndex) return;

        Groups.Move(fromIndex, toIndex);
        int cfgFrom = fromIndex - 1;
        int cfgTo = toIndex - 1;
        var item = _config.Groups[cfgFrom];
        _config.Groups.RemoveAt(cfgFrom);
        _config.Groups.Insert(cfgTo, item);
        _configStore.Save(_config);
    }

    public bool DeleteGroup(string groupId)
    {
        if (groupId == HoldingGroupId) return false;
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;

        // 「自选」不可删
        if (group.Name == DefaultGroupName) return false;

        var fallback = Groups.FirstOrDefault(g => g.Name == DefaultGroupName)
            ?? Groups.First(g => g.Id != groupId && g.Id != HoldingGroupId);

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
        if (groupId == HoldingGroupId || string.IsNullOrEmpty(groupId))
            groupId = Groups.FirstOrDefault(g => g.Name == DefaultGroupName)?.Id
                ?? Groups.FirstOrDefault(g => g.Id != HoldingGroupId)?.Id
                ?? string.Empty;

        var entry = new WatchlistEntry
        {
            Code = code,
            Name = name,
            Market = market,
            GroupId = groupId
        };

        if (ConfigStore.IsFundEntry(entry) || IsFundGroup)
        {
            if (!entry.Code.StartsWith("FD", StringComparison.OrdinalIgnoreCase))
                entry.Code = EastMoneyClient.ToFundInternalCode(entry.Code);
            entry.Market = "基金";
            string fundGroupId = Groups.FirstOrDefault(g => g.Name == FundGroupName)?.Id ?? groupId;
            if (IsFundGroup)
                entry.GroupId = fundGroupId;
            _funds.Items.Add(entry);
            SaveFunds();
        }
        else
        {
            _config.Watchlist.Add(entry);
            _configStore.Save(_config);
        }

        Stocks.Add(ToStockItem(entry));
        RefreshVisibleStocks();
        _ = _intradayService.LoadAllAsync();
    }

    // 按截图合并持仓：已有则更新数量/成本，没有则加入自选并写入持仓；不删除原持仓
    public int SyncHoldingsFromImport(IReadOnlyList<HoldingImportItem> items)
    {
        if (items.Count == 0) return 0;

        string defaultGroupId = Groups.FirstOrDefault(g => g.Name == DefaultGroupName)?.Id
            ?? Groups.FirstOrDefault(g => g.Id != HoldingGroupId)?.Id
            ?? string.Empty;

        int synced = 0;
        bool watchlistChanged = false;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.InternalCode) || item.Shares <= 0 || item.Cost <= 0)
                continue;

            var existing = Stocks.FirstOrDefault(s =>
                !s.IsFund
                && string.Equals(s.Code, item.InternalCode, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                var entry = new WatchlistEntry
                {
                    Code = item.InternalCode,
                    Name = item.Name,
                    Market = item.Market,
                    GroupId = defaultGroupId,
                    HoldingShares = item.Shares,
                    HoldingCost = item.Cost
                };
                _config.Watchlist.Add(entry);
                Stocks.Add(ToStockItem(entry));
                watchlistChanged = true;
                synced++;
                continue;
            }

            existing.HoldingShares = item.Shares;
            existing.HoldingCost = item.Cost;
            int cfgIndex = FindWatchlistIndex(existing);
            if (cfgIndex >= 0)
            {
                _config.Watchlist[cfgIndex].HoldingShares = item.Shares;
                _config.Watchlist[cfgIndex].HoldingCost = item.Cost;
                watchlistChanged = true;
            }
            if (ClearOtherStockHoldings(existing))
                watchlistChanged = true;
            synced++;
        }

        if (watchlistChanged)
            _configStore.Save(_config);

        OnPropertyChanged(nameof(HoldingMarketValueText));
        OnPropertyChanged(nameof(HoldingProfitTotalText));
        OnPropertyChanged(nameof(HoldingTodayProfitText));
        OnPropertyChanged(nameof(HoldingTodayProfitDetails));
        RefreshVisibleStocks();
        _ = _intradayService.LoadAllAsync();
        return synced;
    }

    // 按基金详情截图合并持仓：已有则更新份额/成本单价，没有则加入基金组；不删除原持仓
    public int SyncFundHoldingsFromImport(IReadOnlyList<HoldingImportItem> items)
    {
        if (items.Count == 0) return 0;

        string fundGroupId = Groups.FirstOrDefault(g => g.Name == FundGroupName)?.Id
            ?? Groups.FirstOrDefault(g => g.Id != HoldingGroupId)?.Id
            ?? string.Empty;

        int synced = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.InternalCode) || item.Shares <= 0 || item.Cost <= 0)
                continue;

            string code = item.InternalCode.StartsWith("FD", StringComparison.OrdinalIgnoreCase)
                ? item.InternalCode
                : EastMoneyClient.ToFundInternalCode(item.InternalCode);

            var existing = Stocks.FirstOrDefault(s =>
                s.IsFund
                && string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                var entry = new WatchlistEntry
                {
                    Code = code,
                    Name = item.Name,
                    Market = string.IsNullOrWhiteSpace(item.Market) ? "基金" : item.Market,
                    GroupId = fundGroupId
                };
                _funds.Items.Add(entry);
                Stocks.Add(ToStockItem(entry));
                existing = Stocks.First(s =>
                    s.IsFund
                    && string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
            }

            SetHolding(existing, item.Shares, item.Cost);
            synced++;
        }

        return synced;
    }

    public void RemoveStock(StockItem stock)
    {
        int index = Stocks.IndexOf(stock);
        if (index < 0) return;

        if (SelectedStock == stock)
            SelectedStock = null;

        Stocks.RemoveAt(index);
        if (IsFundStock(stock))
        {
            int fundIndex = FindFundIndex(stock);
            if (fundIndex >= 0)
                _funds.Items.RemoveAt(fundIndex);
            _funds.DcaPlans.RemoveAll(p =>
                string.Equals(p.FundCode, stock.Code, StringComparison.OrdinalIgnoreCase));
            SaveFunds();
        }
        else
        {
            int cfgIndex = FindWatchlistIndex(stock);
            if (cfgIndex >= 0)
                _config.Watchlist.RemoveAt(cfgIndex);
            _configStore.Save(_config);
        }

        RefreshVisibleStocks();
    }

    public void RenameStock(StockItem stock, string customName)
    {
        int index = Stocks.IndexOf(stock);
        if (index < 0) return;

        stock.CustomName = customName;
        if (IsFundStock(stock))
        {
            int fundIndex = FindFundIndex(stock);
            if (fundIndex >= 0)
                _funds.Items[fundIndex].CustomName = customName;
            SaveFunds();
        }
        else
        {
            int cfgIndex = FindWatchlistIndex(stock);
            if (cfgIndex >= 0)
                _config.Watchlist[cfgIndex].CustomName = customName;
            _configStore.Save(_config);
        }

        if (ReferenceEquals(SelectedStock, stock))
            OnPropertyChanged(nameof(SelectedStock));
    }

    public void SetHolding(StockItem stock, decimal shares, decimal cost)
    {
        int index = Stocks.IndexOf(stock);
        if (index < 0) return;

        stock.HoldingShares = shares;
        stock.HoldingCost = cost;

        if (IsFundStock(stock))
        {
            int fundIndex = FindFundIndex(stock);
            if (fundIndex >= 0)
            {
                _funds.Items[fundIndex].HoldingShares = shares;
                _funds.Items[fundIndex].HoldingCost = cost;
            }

            _funds.Trades.RemoveAll(t =>
                string.Equals(t.FundCode, stock.Code, StringComparison.OrdinalIgnoreCase)
                && t.Source == TradeSource.Manual);

            if (shares > 0 && cost > 0)
            {
                _funds.Trades.Add(new TradeRecord
                {
                    FundCode = stock.Code,
                    Date = DateTime.Today.ToString("yyyy-MM-dd"),
                    Side = TradeSide.Buy,
                    Amount = shares * cost,
                    Shares = shares,
                    Nav = cost,
                    Source = TradeSource.Manual
                });
            }

            SaveFunds();
        }
        else
        {
            int cfgIndex = FindWatchlistIndex(stock);
            if (cfgIndex >= 0)
            {
                _config.Watchlist[cfgIndex].HoldingShares = shares;
                _config.Watchlist[cfgIndex].HoldingCost = cost;
            }
            ClearOtherStockHoldings(stock);
            _configStore.Save(_config);
        }

        OnPropertyChanged(nameof(HoldingMarketValueText));
        OnPropertyChanged(nameof(HoldingProfitTotalText));
        OnPropertyChanged(nameof(HoldingTodayProfitText));
        OnPropertyChanged(nameof(HoldingTodayProfitDetails));
        RefreshVisibleStocks();
    }

    // 清除同代码其它条目上的持仓，避免持仓页重复
    private bool ClearOtherStockHoldings(StockItem keep)
    {
        bool changed = false;
        foreach (var s in Stocks)
        {
            if (s.IsFund || ReferenceEquals(s, keep)) continue;
            if (!string.Equals(s.Code, keep.Code, StringComparison.OrdinalIgnoreCase)) continue;
            if (!s.HasHolding && s.HoldingShares == 0 && s.HoldingCost == 0) continue;

            s.HoldingShares = 0;
            s.HoldingCost = 0;
            int cfgIndex = FindWatchlistIndex(s);
            if (cfgIndex >= 0)
            {
                _config.Watchlist[cfgIndex].HoldingShares = 0;
                _config.Watchlist[cfgIndex].HoldingCost = 0;
                changed = true;
            }
        }
        return changed;
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
        if (string.IsNullOrEmpty(groupId) || groupId == HoldingGroupId) return false;
        if (Groups.All(g => g.Id != groupId)) return false;
        if (HasStockInGroup(stock.Code, groupId)) return false;

        // 自选中加入其他分组：复制一份，保留自选中的原条目
        string? zixuanId = Groups.FirstOrDefault(g => g.Name == DefaultGroupName)?.Id;
        if (!string.IsNullOrEmpty(zixuanId) && stock.GroupId == zixuanId)
            return CopyStockToGroup(stock, groupId);

        int index = Stocks.IndexOf(stock);
        if (index < 0 || stock.GroupId == groupId) return false;

        if (IsFundStock(stock))
        {
            int fundIndex = FindFundIndex(stock);
            stock.GroupId = groupId;
            if (fundIndex >= 0)
                _funds.Items[fundIndex].GroupId = groupId;
            SaveFunds();
        }
        else
        {
            int cfgIndex = FindWatchlistIndex(stock);
            stock.GroupId = groupId;
            if (cfgIndex >= 0)
                _config.Watchlist[cfgIndex].GroupId = groupId;
            _configStore.Save(_config);
        }

        RefreshVisibleStocks();
        return true;
    }

    // 复制股票到目标分组（自选保留原条目）
    public bool CopyStockToGroup(StockItem stock, string groupId)
    {
        if (string.IsNullOrEmpty(groupId) || groupId == HoldingGroupId) return false;
        if (Groups.All(g => g.Id != groupId)) return false;
        if (HasStockInGroup(stock.Code, groupId)) return false;

        var entry = new WatchlistEntry
        {
            Code = stock.Code,
            Name = stock.Name,
            Market = stock.Market,
            CustomName = stock.CustomName,
            GroupId = groupId,
            HoldingShares = stock.HoldingShares,
            HoldingCost = stock.HoldingCost
        };

        if (IsFundStock(stock) || ConfigStore.IsFundEntry(entry))
        {
            _funds.Items.Add(entry);
            SaveFunds();
        }
        else
        {
            _config.Watchlist.Add(entry);
            _configStore.Save(_config);
        }

        Stocks.Add(new StockItem
        {
            Name = stock.Name,
            Code = stock.Code,
            Market = stock.Market,
            CustomName = stock.CustomName,
            GroupId = groupId,
            HoldingShares = stock.HoldingShares,
            HoldingCost = stock.HoldingCost,
            CurrentPrice = stock.CurrentPrice,
            ChangePercent = stock.ChangePercent,
            YesterdayClose = stock.YesterdayClose
        });
        _ = _intradayService.LoadAllAsync();
        return true;
    }

    public bool HasStockInGroup(string code, string groupId) =>
        Stocks.Any(s => s.GroupId == groupId
            && s.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public bool RemoveStockFromCurrentGroup(StockItem stock)
    {
        if (SelectedGroupId == HoldingGroupId) return false;
        var target = Groups.FirstOrDefault(g =>
            g.Id != stock.GroupId && g.Id != HoldingGroupId);
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

        if (IsFundStock(dragged))
        {
            int fromFund = FindFundIndex(dragged);
            int toFund = FindFundIndex(target);
            if (fromFund >= 0 && toFund >= 0)
            {
                var item = _funds.Items[fromFund];
                _funds.Items.RemoveAt(fromFund);
                _funds.Items.Insert(toFund, item);
                SaveFunds();
            }
        }
        else
        {
            int fromCfg = FindWatchlistIndex(dragged);
            int toCfg = FindWatchlistIndex(target);
            if (fromCfg >= 0 && toCfg >= 0)
            {
                var item = _config.Watchlist[fromCfg];
                _config.Watchlist.RemoveAt(fromCfg);
                _config.Watchlist.Insert(toCfg, item);
                _configStore.Save(_config);
            }
        }

        RefreshVisibleStocks();
    }

    private int FindWatchlistIndex(StockItem stock)
    {
        return _config.Watchlist.FindIndex(e => ReferenceMatches(e, stock));
    }

    private int FindFundIndex(StockItem stock) =>
        _funds.Items.FindIndex(e => ReferenceMatches(e, stock));

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
            "holdingAmount" => s => s.HoldingAmount,
            "holdingShares" => s => s.HoldingShares,
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

    public void SaveSettings(
        double opacity,
        int fontSize,
        int refreshInterval,
        string hotkey,
        bool topmost,
        bool showMarketTag,
        string holdingVisionProvider,
        string deepSeekApiKey,
        string mimoApiKey)
    {
        _config.Opacity = opacity;
        _config.FontSize = fontSize;
        _config.RefreshInterval = refreshInterval;
        _config.Hotkey = hotkey;
        _config.Topmost = topmost;
        _config.ShowMarketTag = showMarketTag;
        _config.HoldingVisionProvider = string.IsNullOrWhiteSpace(holdingVisionProvider)
            ? "deepseek"
            : holdingVisionProvider.Trim().ToLowerInvariant();
        _config.DeepSeekApiKey = deepSeekApiKey ?? "";
        _config.MimoApiKey = mimoApiKey ?? "";
        _configStore.Save(_config);

        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(RefreshInterval));
        OnPropertyChanged(nameof(Hotkey));
        OnPropertyChanged(nameof(Topmost));
        OnPropertyChanged(nameof(ShowMarketTag));

        UpdateTimers();
    }

    // 获取持仓截图识别配置；未配置 Key 时返回 null
    public HoldingVisionOptions? GetHoldingVisionOptions()
    {
        string provider = string.IsNullOrWhiteSpace(_config.HoldingVisionProvider)
            ? "deepseek"
            : _config.HoldingVisionProvider.Trim().ToLowerInvariant();
        string key = provider == "mimo" ? _config.MimoApiKey : _config.DeepSeekApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return null;
        return new HoldingVisionOptions { Provider = provider, ApiKey = key.Trim() };
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

// 今日盈亏提示行
public sealed record HoldingTooltipItem(string Name, string ChangeText);
