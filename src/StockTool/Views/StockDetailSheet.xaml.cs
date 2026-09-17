using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StockTool.Core.Models;
using StockTool.Data;
using StockTool.Services;
using StockTool.ViewModels;

namespace StockTool.Views;

public partial class StockDetailSheet : UserControl
{
    private EastMoneyClient? _client;
    private MainViewModel? VM => DataContext as MainViewModel;

    /// <summary>遮罩按下/松手后通知主窗口短暂屏蔽列表点击穿透。</summary>
    public event Action<bool>? SuppressListInputChanged;

    public StockDetailSheet()
    {
        InitializeComponent();
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

    private async Task LoadIntradayAsync(StockItem stock)
    {
        if (_client == null) return;
        try
        {
            var series = await _client.GetIntradayAsync(stock.Code);
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
            var data = await _client.GetKlineAsync(stock.Code, stock.KlinePeriod);
            if (data.Count > 0)
                stock.KlineData = data;
        }
        catch { }
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
            await LoadIntradayAsync(stock);
            return;
        }

        stock.KlineData = [];
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
