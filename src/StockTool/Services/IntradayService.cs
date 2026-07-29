using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using StockTool.Core.Models;
using StockTool.Data;

namespace StockTool.Services;

public class IntradayService
{
    private readonly EastMoneyClient _client;
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<StockItem> _stocks;

    public IntradayService(EastMoneyClient client, ObservableCollection<StockItem> stocks)
    {
        _client = client;
        _stocks = stocks;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += async (s, e) => await RefreshAllAsync();
    }

    public async Task LoadAllAsync()
    {
        await RefreshAllAsync();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void SetInterval(int seconds) => _timer.Interval = TimeSpan.FromSeconds(seconds);

    private async Task RefreshAllAsync()
    {
        var codes = _stocks.Select(s => s.Code).ToList();

        foreach (var code in codes)
        {
            IntradaySeries? series;
            try
            {
                series = await _client.GetIntradayAsync(code);
            }
            catch
            {
                continue;
            }

            if (series == null || series.Prices.Count == 0) continue;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var stock = _stocks.FirstOrDefault(s => s.Code == code);
                if (stock == null) return;
                stock.IntradayPoints = series.Prices;
                stock.IntradayAvgPoints = series.AvgPrices;
            });
        }
    }
}
