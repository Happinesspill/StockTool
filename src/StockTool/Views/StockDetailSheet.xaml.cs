using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StockTool.Core.Indicators;
using StockTool.Core.Models;
using StockTool.Data;
using StockTool.Services;
using StockTool.ViewModels;

namespace StockTool.Views;

public partial class StockDetailSheet : UserControl
{
    private EastMoneyClient? _client;
    private MainViewModel? VM => DataContext as MainViewModel;
    // 现价 → 图表末根同步（分时/各周期K线买卖点随现价重算）
    private readonly System.Windows.Threading.DispatcherTimer _priceSyncTimer;

    /// <summary>遮罩按下/松手后通知主窗口短暂屏蔽列表点击穿透。</summary>
    public event Action<bool>? SuppressListInputChanged;

    public StockDetailSheet()
    {
        InitializeComponent();

        _priceSyncTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _priceSyncTimer.Tick += OnPriceSyncTick;
        _priceSyncTimer.Start();
    }

    public void Initialize(EastMoneyClient client)
    {
        _client = client;
    }

    public async Task ShowAsync(StockItem stock)
    {
        await LoadDetailChartAsync(stock);
        AnimateSheetSlideUp(fromOffset: 72);
    }

    private void BtnExpandDetail_Click(object sender, RoutedEventArgs e)
    {
        if (VM == null) return;
        if (VM.SelectedStock?.IsFund == true)
        {
            e.Handled = true;
            return;
        }

        bool expanding = !VM.IsChartExpanded;
        VM.IsChartExpanded = expanding;
        if (expanding)
            AnimateSheetSlideUp(fromOffset: 72);
        e.Handled = true;
    }

    private void BtnCollapseDetail_Click(object sender, RoutedEventArgs e)
    {
        if (VM == null) return;
        if (VM.IsChartExpanded)
        {
            VM.IsChartExpanded = false;
            e.Handled = true;
            return;
        }
        CloseDetailSheet();
        e.Handled = true;
    }

    private void BtnCloseDetail_Click(object sender, RoutedEventArgs e)
    {
        CloseDetailSheet();
    }

    private void AnimateSheetSlideUp(double fromOffset)
    {
        if (DetailSheetTransform == null) return;

        var anim = new DoubleAnimation
        {
            From = fromOffset,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        DetailSheetTransform.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    private void DetailMask_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SuppressListInputChanged?.Invoke(true);
        e.Handled = true;
    }

    private void DetailMask_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CloseDetailSheet();
        e.Handled = true;
        Dispatcher.BeginInvoke(() => SuppressListInputChanged?.Invoke(false));
    }

    private void DetailSheet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    public void Close() => CloseDetailSheet();

    private void CloseDetailSheet()
    {
        if (VM == null) return;
        VM.IsChartExpanded = false;
        VM.SelectedStock = null;
    }

    private async Task LoadDetailChartAsync(StockItem stock)
    {
        if (stock.IsFund)
        {
            await LoadFundNavAsync(stock);
            return;
        }

        if (stock.KlinePeriod == 0)
        {
            if (stock.IntradayPoints.Count == 0)
                await LoadIntradayAsync(stock);
            return;
        }

        if (stock.KlineData.Count == 0)
            await LoadKlineAsync(stock);
    }

    private async Task LoadFundNavAsync(StockItem stock)
    {
        if (VM == null) return;
        try
        {
            var history = await VM.FundNav.GetHistoryAsync(stock.Code);
            var chart = FundNavService.BuildChart(history, stock.FundNavRange, out _);
            stock.FundNavChart = chart;

            var markers = VM.GetFundTrades(stock.Code)
                .Where(t => DateTime.TryParse(t.Date, out _))
                .Select(t => new FundTradeMarker
                {
                    Date = DateTime.Parse(t.Date).Date,
                    Side = t.Side,
                    Amount = t.Amount,
                    Shares = t.Shares
                })
                .ToList();
            stock.FundTradeMarkers = markers;

            if (chart.Count > 0)
            {
                stock.FundNavHoverDate = chart[^1].Date.ToString("yyyy-MM-dd");
                stock.FundNavHoverReturn = $"{chart[^1].ReturnPercent:+0.00;-0.00;0.00}%";
            }
            else
            {
                stock.FundNavHoverDate = "";
                stock.FundNavHoverReturn = "";
            }
        }
        catch
        {
            stock.FundNavChart = [];
            stock.FundTradeMarkers = [];
        }
    }

    // 每秒用现价刷新末根（分时/各周期K线），买卖点随之重算重绘
    private void OnPriceSyncTick(object? sender, EventArgs e)
    {
        if (VM?.SelectedStock is not { } stock) return;

        decimal price = stock.CurrentPrice;
        if (price <= 0) return;

        if (stock.KlinePeriod == 0)
        {
            // 分时：现价接到最后一个点
            if (stock.IntradayPoints.Count > 0)
                IntradayChart.SyncLastPrice(price);
            return;
        }

        if (stock.KlineData.Count == 0) return;

        // K线：刷新正在形成的那根，控件内部重算指标与 MACD+KDJ 买卖点
        decimal before = stock.KlineData[^1].Close;
        KlineChart.SyncLastBar(price);
        if (stock.KlineData[^1].Close == before) return;

        // 日线：红利策略同样按新收盘价重算（周/月/分红用缓存，不再发请求）
        if (stock.KlinePeriod == 101
            && stock.StrategyWeekly.Count > 0
            && stock.StrategyMonthly.Count > 0)
        {
            var result = DividendBollStrategy.Evaluate(
                stock.KlineData, stock.StrategyWeekly, stock.StrategyMonthly, stock.StrategyDividends);
            stock.StrategySignals = result.Signals;
            stock.StrategySummary = result.Summary;
        }
    }

    private async Task LoadIntradayAsync(StockItem stock)
    {
        if (_client == null) return;
        try
        {
            // 预热数据与当日分时并行拉取；预热只用于指标对齐，失败不影响主图
            var warmupTask = stock.IntradayWarmupPoints.Count > 0
                ? Task.FromResult(stock.IntradayWarmupPoints)
                : _client.GetIntradayWarmupAsync(stock.Code);

            var series = await _client.GetIntradayAsync(stock.Code);

            // 先落预热再落当日点，避免先画出无预热的半成品
            try
            {
                var warmup = await warmupTask;
                if (warmup.Count > 0) stock.IntradayWarmupPoints = warmup;
            }
            catch { }

            if (series != null && series.Prices.Count > 0)
            {
                stock.IntradayPoints = series.Prices;
                stock.IntradayAvgPoints = series.AvgPrices;
                stock.IntradayTimes = series.Times;
            }
        }
        catch { }
    }

    private async Task LoadKlineAsync(StockItem stock)
    {
        if (_client == null) return;
        try
        {
            // 1 分钟线取当日整段（约 240~330 根），其余周期用默认根数
            int count = stock.KlinePeriod == 1 ? 300 : 160;
            var data = await _client.GetKlineAsync(stock.Code, stock.KlinePeriod, count);
            if (data.Count > 0)
                stock.KlineData = data;

            // 红利策略基于日/周/月 BOLL，只在日线视图计算
            if (stock.KlinePeriod == 101 && data.Count > 0)
                await LoadDividendStrategyAsync(stock, data);
        }
        catch { }
    }

    // 拉周线/月线/分红，算多周期 BOLL 共振买卖点
    private async Task LoadDividendStrategyAsync(StockItem stock, List<KlineItem> daily)
    {
        if (_client == null) return;
        try
        {
            var weeklyTask = _client.GetKlineAsync(stock.Code, 102, 160);
            var monthlyTask = _client.GetKlineAsync(stock.Code, 103, 80);
            var dividendTask = _client.GetDividendEventsAsync(stock.Code);

            var weekly = await weeklyTask;
            var monthly = await monthlyTask;
            var dividends = await dividendTask;

            // 缓存周/月/分红，供盘中按现价重算策略
            stock.StrategyWeekly = weekly;
            stock.StrategyMonthly = monthly;
            stock.StrategyDividends = dividends;

            var result = DividendBollStrategy.Evaluate(daily, weekly, monthly, dividends);
            stock.StrategySignals = result.Signals;
            stock.StrategySummary = result.Summary;
        }
        catch
        {
            stock.StrategySignals = [];
            stock.StrategySummary = string.Empty;
        }
    }

    private async void KlinePeriod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.Tag is not string periodStr) return;
        if (VM?.SelectedStock is not { } stock) return;
        if (stock.IsFund) return;

        int period = int.Parse(periodStr);
        if (stock.KlinePeriod == period) return;

        stock.KlinePeriod = period;

        if (period == 0)
        {
            // 分时视图不显示红利策略标记
            stock.StrategySignals = [];
            stock.StrategySummary = string.Empty;
            await LoadIntradayAsync(stock);
            return;
        }

        stock.KlineData = [];
        // 策略标记只对日线有意义，切到分钟/周线时清空避免错位
        if (period != 101)
        {
            stock.StrategySignals = [];
            stock.StrategySummary = string.Empty;
        }
        await LoadKlineAsync(stock);
    }

    private async void FundNavRange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.Tag is not string range) return;
        if (VM?.SelectedStock is not { } stock) return;
        if (!stock.IsFund) return;
        if (stock.FundNavRange == range) return;

        stock.FundNavRange = range;
        await LoadFundNavAsync(stock);
    }
}
