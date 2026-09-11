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
        // 场内 ETF/LOF 先归一化为行情代码（FD513310 → SH513310）再取分时
        var targets = _stocks
            .Select(s => (Stock: s, Code: s.IsExchangeFund ? StockItem.ToExchangeCode(s.Code) : s.Code))
            .ToList();

        foreach (var (stock, code) in targets)
        {
            if (EastMoneyClient.IsFundCode(code)) continue;   // 场外基金无分时

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
                stock.IntradayPoints = series.Prices;
                stock.IntradayAvgPoints = series.AvgPrices;
            });
        }
    }
}
