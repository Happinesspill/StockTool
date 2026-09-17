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
    private readonly ObservableCollection<IndexItem> _indices;
    private readonly Func<int> _getInterval;
    private readonly Action? _onUpdated;
    private int _tickGuard;

    public QuoteService(
        EastMoneyClient client,
        ObservableCollection<StockItem> stocks,
        ObservableCollection<IndexItem> indices,
        Func<int> getInterval,
        Action? onUpdated = null)
    {
        _client = client;
        _stocks = stocks;
        _indices = indices;
        _getInterval = getInterval;
        _onUpdated = onUpdated;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (s, e) => await TickAsync();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void UpdateInterval() => _timer.Interval = TimeSpan.FromSeconds(_getInterval());

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _tickGuard, 1) == 1) return;

        try
        {
            var fundCodes = _stocks
                .Where(s => s.IsFund && !string.IsNullOrEmpty(s.Code))
                .Select(s => s.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // 场内 ETF/LOF 统一归一化为 SH/SZ 前缀，才能被行情接口识别（FD513310 → SH513310）
            var stockCodes = _stocks
                .Where(s => !s.IsFund && !string.IsNullOrEmpty(s.Code))
                .Select(s => s.IsExchangeFund ? StockItem.ToExchangeCode(s.Code) : s.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var indexCodes = _indices.Select(i => i.Code)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (stockCodes.Count == 0 && indexCodes.Count == 0 && fundCodes.Count == 0) return;

            // A 股指数走腾讯/新浪（GetBatchQuotesAsync）；IX 全球指数走东财 push2
            var domesticIndexCodes = indexCodes
                .Where(c => !c.StartsWith("IX", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var globalIndexCodes = indexCodes
                .Where(c => c.StartsWith("IX", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var batchCodes = stockCodes
                .Concat(domesticIndexCodes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Task<List<QuoteItem>> batchTask = batchCodes.Count > 0
                ? _client.GetBatchQuotesAsync(batchCodes)
                : Task.FromResult(new List<QuoteItem>());
            Task<List<QuoteItem>> globalTask = globalIndexCodes.Count > 0
                ? _client.GetPush2QuotesAsync(globalIndexCodes)
                : Task.FromResult(new List<QuoteItem>());
            Task<List<QuoteItem>> fundTask = fundCodes.Count > 0
                ? _client.GetFundValuationsAsync(fundCodes)
                : Task.FromResult(new List<QuoteItem>());

            List<QuoteItem> batchResults = [];
            List<QuoteItem> globalResults = [];
            List<QuoteItem> fundResults = [];
            try { batchResults = await batchTask; } catch { /* keep last */ }
            try { globalResults = await globalTask; } catch { /* keep last */ }
            try { fundResults = await fundTask; } catch { /* keep last */ }

            var quoteMap = BuildQuoteMap(batchResults);
            foreach (var r in globalResults)
            {
                if (string.IsNullOrEmpty(r.F12)) continue;
                try
                {
                    string key = EastMoneyClient.InternalCodeFromSecId($"{r.F13}.{r.F12}");
                    quoteMap[key] = r;
                }
                catch
                {
                    // ignore
                }
                quoteMap[$"{r.F13}.{r.F12}"] = r;
            }

            var fundMap = new Dictionary<string, QuoteItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in fundResults)
            {
                if (string.IsNullOrEmpty(r.F12)) continue;
                fundMap[r.F12] = r;
                fundMap[EastMoneyClient.ToFundInternalCode(r.F12)] = r;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            await dispatcher.InvokeAsync(() =>
            {
                foreach (var stock in _stocks)
                {
                    if (stock.IsFund)
                    {
                        if (!fundMap.TryGetValue(stock.Code, out var fundQuote)
                            && !fundMap.TryGetValue(stock.CodeNumeric, out fundQuote))
                            continue;

                        if (!string.IsNullOrEmpty(fundQuote.F14))
                            stock.Name = fundQuote.F14;
                        stock.CurrentPrice = fundQuote.F2;
                        stock.ChangePercent = fundQuote.F3;
                        stock.YesterdayClose = fundQuote.F17;
                        stock.HasFundValuation = fundQuote.HasEstimate;
                        continue;
                    }

                    if (!TryGetQuote(quoteMap, stock.Code, stock.CodeNumeric, out var quote))
                        continue;

                    stock.Name = quote.F14 ?? stock.Name;
                    stock.CurrentPrice = quote.F2;
                    stock.ChangePercent = quote.F3;
                    stock.YesterdayClose = quote.F17;
                    stock.Open = quote.F18;
                    stock.High = quote.F15;
                    stock.Low = quote.F16;
                    stock.Volume = quote.F5;
                    stock.Turnover = quote.F6;
                    stock.TurnoverRate = quote.F8;
                    stock.VolumeRatio = quote.F10;
                    stock.TotalMarketValue = quote.F20;
                    stock.FloatMarketValue = quote.F21;
                    stock.Speed = quote.F22;
                }

                foreach (var index in _indices)
                {
                    if (!TryResolveIndexQuote(quoteMap, index.Code, out var quote))
                        continue;

                    index.Price = quote.F2;
                    index.ChangePercent = quote.F3;
                    index.Change = quote.F4 != 0
                        ? quote.F4
                        : (quote.F17 > 0 ? quote.F2 - quote.F17 : 0);
                }

                _onUpdated?.Invoke();
            });
        }
        finally
        {
            Interlocked.Exchange(ref _tickGuard, 0);
        }
    }

    private static bool TryResolveIndexQuote(
        Dictionary<string, QuoteItem> map,
        string code,
        out QuoteItem quote)
    {
        string numeric = code.Length > 2 ? code[2..] : code;
        // IX100.NDX → numeric 应为 NDX
        if (code.StartsWith("IX", StringComparison.OrdinalIgnoreCase) && code.Contains('.'))
            numeric = code[(code.LastIndexOf('.') + 1)..];

        if (TryGetQuote(map, code, numeric, out quote))
            return true;

        try
        {
            string secid = EastMoneyClient.ToSecId(code);
            if (map.TryGetValue(secid, out quote!))
                return true;
        }
        catch
        {
            // ignore
        }

        quote = null!;
        return false;
    }

    private static Dictionary<string, QuoteItem> BuildQuoteMap(List<QuoteItem> results)
    {
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
            map[$"{r.F13}.{r.F12}"] = r;

            if (key.StartsWith("HK", StringComparison.OrdinalIgnoreCase))
            {
                string digits = r.F12 ?? "";
                map[$"HK{digits.PadLeft(5, '0')}"] = r;
                map[$"HK{digits.TrimStart('0').PadLeft(1, '0')}"] = r;
                if (digits.Length > 0)
                    map[$"HK{digits}"] = r;
            }
        }
        return map;
    }

    private static bool TryGetQuote(
        Dictionary<string, QuoteItem> map,
        string code,
        string codeNumeric,
        out QuoteItem quote)
    {
        if (map.TryGetValue(code, out quote!))
            return true;

        quote = map.Values.FirstOrDefault(q =>
            string.Equals(q.F12, codeNumeric, StringComparison.OrdinalIgnoreCase)
            || string.Equals(q.F12?.PadLeft(6, '0'), codeNumeric.PadLeft(6, '0'), StringComparison.OrdinalIgnoreCase))!;
        return quote != null;
    }

    public Task RefreshNowAsync() => TickAsync();
}
