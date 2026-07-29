using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using StockTool.Core.Models;
using StockTool.Data;

namespace StockTool.Services;

public class QuoteService
{
    private readonly EastMoneyClient _client;
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<StockItem> _stocks;
    private readonly Func<int> _getInterval;
    private int _tickGuard;

    public QuoteService(EastMoneyClient client, ObservableCollection<StockItem> stocks, Func<int> getInterval)
    {
        _client = client;
        _stocks = stocks;
        _getInterval = getInterval;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (s, e) => await TickAsync();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void UpdateInterval() => _timer.Interval = TimeSpan.FromSeconds(_getInterval());

    private async Task TickAsync()
    {
        // 防重入：上一次请求未完成时跳过本次
        if (Interlocked.Exchange(ref _tickGuard, 1) == 1) return;

        try
        {
            // capture codes on UI thread before any await
            var codes = _stocks.Select(s => s.Code).ToList();
            if (codes.Count == 0) return;

            List<QuoteItem>? results;
            try
            {
                results = await _client.GetBatchQuotesAsync(codes);
            }
            catch
            {
                return; // silent failure, keep last data
            }

            var map = new Dictionary<string, QuoteItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in results)
            {
                if (string.IsNullOrEmpty(r.F12)) continue;
                string key;
                try
                {
                    key = EastMoneyClient.InternalCodeFromSecId($"{r.F13}.{r.F12}");
                }
                catch
                {
                    continue;
                }
                map[key] = r;

                // 兼容港股补零差异：HK1810 / HK01810
                if (key.StartsWith("HK", StringComparison.OrdinalIgnoreCase))
                {
                    string digits = r.F12 ?? "";
                    map[$"HK{digits.PadLeft(5, '0')}"] = r;
                    map[$"HK{digits.TrimStart('0').PadLeft(1, '0')}"] = r; // 保留至少 1 位
                    if (digits.Length > 0)
                        map[$"HK{digits}"] = r;
                }
            }

            // dispatch all UI updates to the UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var stock in _stocks)
                {
                    if (!map.TryGetValue(stock.Code, out var quote))
                    {
                        // 再试：去掉前缀只比数字代码
                        quote = map.Values.FirstOrDefault(q =>
                            string.Equals(q.F12, stock.CodeNumeric, StringComparison.OrdinalIgnoreCase));
                        if (quote == null) continue;
                    }

                    stock.Name = quote.F14 ?? stock.Name;
                    stock.CurrentPrice = quote.F2;
                    stock.ChangePercent = quote.F3;
                    stock.YesterdayClose = quote.F17;
                    stock.Open = quote.F18;
                    stock.High = quote.F15;
                    stock.Low = quote.F16;
                    stock.Volume = quote.F5;
                    stock.Turnover = quote.F6;
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref _tickGuard, 0);
        }
    }

    /// <summary>立即拉一次行情（启动时用）</summary>
    public Task RefreshNowAsync() => TickAsync();
}
