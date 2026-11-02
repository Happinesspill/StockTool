using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using StockTool.Core.Models;
using StockTool.Data;

namespace StockTool.Services;

public class IntradayService
{
    private const int MaxConcurrency = 8;

    private readonly EastMoneyClient _client;
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<StockItem> _stocks;
    private int _refreshGuard;
    private int _refreshRequested;

    public IntradayService(EastMoneyClient client, ObservableCollection<StockItem> stocks)
    {
        _client = client;
        _stocks = stocks;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += async (s, e) => await RefreshAllAsync();
    }

    public Task LoadAllAsync()
    {
        Interlocked.Exchange(ref _refreshRequested, 1);
        return RefreshAllAsync();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void SetInterval(int seconds) => _timer.Interval = TimeSpan.FromSeconds(seconds);

    private async Task RefreshAllAsync()
    {
        if (Interlocked.Exchange(ref _refreshGuard, 1) == 1)
        {
            Interlocked.Exchange(ref _refreshRequested, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _refreshRequested, 0);
                await FetchAllAsync().ConfigureAwait(false);
            }
            while (Interlocked.CompareExchange(ref _refreshRequested, 0, 1) == 1);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshGuard, 0);
            if (Interlocked.CompareExchange(ref _refreshRequested, 0, 1) == 1)
                _ = RefreshAllAsync();
        }
    }

    private async Task FetchAllAsync()
    {
        // 场内 ETF/LOF 先归一化为行情代码（FD513310 → SH513310）再取分时
        var targets = _stocks
            .Select(s => (Stock: s, Code: s.IsExchangeFund ? StockItem.ToExchangeCode(s.Code) : s.Code))
            .Where(t => !string.IsNullOrWhiteSpace(t.Code))
            .Where(t => !EastMoneyClient.IsFundCode(t.Code) || StockItem.IsExchangeFundCode(t.Stock.Code))
            .GroupBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (targets.Count == 0) return;

        using var gate = new SemaphoreSlim(MaxConcurrency);
        var tasks = targets.Select(async item =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                IntradaySeries? series;
                try
                {
                    series = await _client.GetIntradayAsync(item.Code).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                if (series == null || series.Prices.Count == 0) return;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                await dispatcher.InvokeAsync(() =>
                {
                    // 同代码可能多分组各有一条，全部写入
                    foreach (var stock in _stocks.Where(s =>
                                 string.Equals(
                                     s.IsExchangeFund ? StockItem.ToExchangeCode(s.Code) : s.Code,
                                     item.Code,
                                     StringComparison.OrdinalIgnoreCase)))
                    {
                        stock.IntradayPoints = series.Prices;
                        stock.IntradayAvgPoints = series.AvgPrices;
                        stock.IntradayTimes = series.Times;
                    }
                });
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
