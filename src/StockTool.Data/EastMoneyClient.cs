using System.Net.Http.Json;
using System.Text.Json;
using StockTool.Core.Models;

namespace StockTool.Data;

public class EastMoneyClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public EastMoneyClient()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        _http = new HttpClient();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://finance.qq.com/");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        _http.Timeout = TimeSpan.FromSeconds(8);
    }

    // ── Market mapping ──────────────────────────────

    public static string ToSecId(string internalCode)
    {
        // 全球指数：IX100.HSI → 100.HSI
        if (internalCode.StartsWith("IX", StringComparison.OrdinalIgnoreCase)
            && internalCode.Length > 2
            && internalCode.Contains('.'))
        {
            return internalCode[2..];
        }

        string prefix = internalCode[..2].ToUpperInvariant();
        string code = internalCode[2..];
        int market = prefix switch
        {
            "SH" => 1,
            "SZ" => 0,
            "HK" => 116,
            _ => throw new ArgumentException($"Unknown market prefix: {prefix}")
        };
        // 港股东财 secid 需 5 位代码，如 116.01810
        if (market == 116)
            code = code.PadLeft(5, '0');
        return $"{market}.{code}";
    }

    public static string InternalCodeFromSecId(string secid)
    {
        var parts = secid.Split('.');
        if (parts.Length < 2)
            throw new ArgumentException($"Invalid secid: {secid}");

        if (!int.TryParse(parts[0], out int market))
        {
            // 非数字市场号时原样拼回 IX 前缀
            return $"IX{secid}";
        }

        if (market is 1 or 0 or 116)
        {
            string prefix = market switch
            {
                1 => "SH",
                0 => "SZ",
                116 => "HK",
                _ => "SH"
            };
            return $"{prefix}{parts[1]}";
        }

        // 全球指数等：100.HSI → IX100.HSI
        return $"IX{parts[0]}.{parts[1]}";
    }

    /// <summary>东财 push2 批量报价（公开给指数等场景）。</summary>
    public Task<List<QuoteItem>> GetPush2QuotesAsync(IReadOnlyList<string> internalCodes)
        => GetEastMoneyQuotesAsync(internalCodes);

    public static bool IsFundCode(string internalCode)
        => internalCode.StartsWith("FD", StringComparison.OrdinalIgnoreCase);

    public static string ToFundInternalCode(string fundCode)
        => $"FD{fundCode.Trim()}";

    public static string MarketLabelFromPrefix(string prefix) => prefix switch
    {
        "SH" => "沪A",
        "SZ" => "深A",
        "HK" => "港股",
        "FD" => "基金",
        _ => prefix
    };

    // ── Batch quotes ────────────────────────────────

    public async Task<List<QuoteItem>> GetBatchQuotesAsync(IReadOnlyList<string> internalCodes)
    {
        if (internalCodes.Count == 0) return [];

        internalCodes = internalCodes.Where(c => !IsFundCode(c)).ToList();
        if (internalCodes.Count == 0) return [];

        // 港股/全球指数：东财 push2；A 股/ETF：腾讯/新浪竞速，东财兜底
        var push2Codes = internalCodes
            .Where(c => c.StartsWith("HK", StringComparison.OrdinalIgnoreCase)
                     || c.StartsWith("IX", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var otherCodes = internalCodes
            .Where(c => !c.StartsWith("HK", StringComparison.OrdinalIgnoreCase)
                     && !c.StartsWith("IX", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (push2Codes.Count > 0 && otherCodes.Count == 0)
            return await GetEastMoneyQuotesAsync(push2Codes);

        if (push2Codes.Count == 0)
            return await GetNonHkQuotesAsync(otherCodes);

        var push2Task = GetEastMoneyQuotesAsync(push2Codes);
        var otherTask = GetNonHkQuotesAsync(otherCodes);
        var parts = await Task.WhenAll(push2Task, otherTask);
        return parts.SelectMany(x => x).ToList();
    }

    private async Task<List<QuoteItem>> GetNonHkQuotesAsync(IReadOnlyList<string> internalCodes)
    {
        if (internalCodes.Count == 0) return [];

        var tencentTask = GetTencentQuotesAsync(internalCodes);
        var sinaTask = GetSinaQuotesAsync(internalCodes);

        var tasks = new List<Task<List<QuoteItem>>> { tencentTask, sinaTask };
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            var result = await completed;
            if (result.Count > 0 && result.Any(r => r.F2 > 0))
                return result;
            tasks.Remove(completed);
        }

        return await GetEastMoneyQuotesAsync(internalCodes);
    }

    /// <summary>
    /// 东方财富 push2 批量报价。A 股指数与全球指数分批请求，避免互相拖垮。
    /// </summary>
    private async Task<List<QuoteItem>> GetEastMoneyQuotesAsync(IReadOnlyList<string> internalCodes)
    {
        if (internalCodes.Count == 0) return [];

        var ixCodes = internalCodes
            .Where(c => c.StartsWith("IX", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var otherCodes = internalCodes
            .Where(c => !c.StartsWith("IX", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ixCodes.Count == 0)
            return await FetchPush2BatchAsync(otherCodes);
        if (otherCodes.Count == 0)
            return await FetchPush2BatchAsync(ixCodes);

        var parts = await Task.WhenAll(
            FetchPush2BatchAsync(otherCodes),
            FetchPush2BatchAsync(ixCodes));
        return parts.SelectMany(x => x).ToList();
    }

    private async Task<List<QuoteItem>> FetchPush2BatchAsync(IReadOnlyList<string> internalCodes)
    {
        if (internalCodes.Count == 0) return [];

        var secids = string.Join(",", internalCodes.Select(ToSecId));
        string url = $"https://push2.eastmoney.com/api/qt/ulist/get" +
                     $"?fltt=2" +
                     $"&invt=2" +
                     $"&fields=f2,f3,f4,f5,f6,f8,f10,f12,f13,f14,f15,f16,f17,f18,f20,f21,f22" +
                     $"&secids={Uri.EscapeDataString(secids)}" +
                     $"&ut=fa5fd1943c7b386f172d6893dbfba10b" +
                     $"&pn=1&np=1&pz=200" +
                     $"&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://quote.eastmoney.com/");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return [];

            var response = await resp.Content.ReadFromJsonAsync<QuoteBatchResponse>(JsonOpts);
            var list = response?.Data?.Diff ?? [];

            // 东财原生：f17=今开 f18=昨收；统一为 QuoteItem 约定：F17=昨收 F18=今开
            foreach (var item in list)
                (item.F17, item.F18) = (item.F18, item.F17);

            return list;
        }
        catch
        {
            return [];
        }
    }

    private async Task<List<QuoteItem>> GetSinaQuotesAsync(IReadOnlyList<string> internalCodes)
    {
        // https://hq.sinajs.cn/list=sh600036,sz000533,hk01810
        var symbols = internalCodes.Select(c => c.ToLowerInvariant());
        string url = $"https://hq.sinajs.cn/list={string.Join(",", symbols)}";

        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            string text;
            try { text = System.Text.Encoding.GetEncoding("GBK").GetString(bytes); }
            catch { text = System.Text.Encoding.UTF8.GetString(bytes); }

            var result = new List<QuoteItem>();
            foreach (var segment in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = segment.IndexOf('=');
                if (eq < 0) continue;
                string varName = segment[..eq].Trim();
                string payload = segment[(eq + 1)..].Trim().Trim('"');
                var f = payload.Split(',');
                if (f.Length < 10) continue;

                bool isHk = varName.Contains("hk", StringComparison.OrdinalIgnoreCase);
                string code;
                decimal price, preClose, open, high, low, pct, volume, turnover;

                if (isHk)
                {
                    // HK: [1]中文名 [2]今开 [3]昨收 [4]最高 [5]最低 [6]现价 [7]涨跌额 [8]涨跌幅
                    //     [11]成交额 [12]成交量
                    if (f.Length < 19) continue;
                    if (!TryParseDec(f[6], out price) || price <= 0) continue;
                    TryParseDec(f[3], out preClose);
                    TryParseDec(f[2], out open);
                    TryParseDec(f[4], out high);
                    TryParseDec(f[5], out low);
                    TryParseDec(f[8], out pct);
                    TryParseDec(f[12], out volume);
                    TryParseDec(f[11], out turnover);
                    code = f[6]; // placeholder, code comes from varName
                }
                else
                {
                    // A-share: [0]名称 [1]今开 [2]昨收 [3]现价 [4]最高 [5]最低 [8]成交量 [9]成交额
                    if (f.Length < 30) continue;
                    if (!TryParseDec(f[3], out price) || price <= 0) continue;
                    TryParseDec(f[2], out preClose);
                    TryParseDec(f[1], out open);
                    TryParseDec(f[4], out high);
                    TryParseDec(f[5], out low);
                    TryParseDec(f[8], out volume);
                    TryParseDec(f[9], out turnover);
                    // A-share 无直接涨跌幅，计算
                    pct = preClose > 0 ? (price - preClose) / preClose * 100m : 0;
                }

                // 从 varName 提取纯数字代码：hq_str_sh600036 → 600036
                string numCode = varName;
                int lastUnderscore = numCode.LastIndexOf('_');
                if (lastUnderscore >= 0) numCode = numCode[(lastUnderscore + 1)..];
                // remove prefix like sh/sz/hk
                if (numCode.Length > 2 && !char.IsDigit(numCode[0]))
                    numCode = numCode[2..];
                code = numCode;

                int market = varName.Contains("sh", StringComparison.OrdinalIgnoreCase) ? 1
                    : varName.Contains("sz", StringComparison.OrdinalIgnoreCase) ? 0
                    : 116;

                result.Add(new QuoteItem
                {
                    F2 = price,
                    F3 = pct,
                    F5 = volume,
                    F6 = turnover,
                    F8 = 0,
                    F10 = 0,
                    F12 = code,
                    F13 = market,
                    F14 = isHk ? f[1] : f[0],
                    F15 = high,
                    F16 = low,
                    F17 = preClose,
                    F18 = open,
                    F20 = 0,
                    F21 = 0,
                    F22 = 0
                });
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    private async Task<List<QuoteItem>> GetTencentQuotesAsync(IReadOnlyList<string> internalCodes)
    {
        // https://qt.gtimg.cn/q=sh600036,hk01810
        var symbols = internalCodes.Select(c => $"{c[..2].ToLowerInvariant()}{c[2..]}");
        string url = $"https://qt.gtimg.cn/q={string.Join(",", symbols)}";

        try
        {
            // 腾讯返回 GBK
            var bytes = await _http.GetByteArrayAsync(url);
            string text;
            try
            {
                text = System.Text.Encoding.GetEncoding("GBK").GetString(bytes);
            }
            catch
            {
                text = System.Text.Encoding.UTF8.GetString(bytes);
            }

            var result = new List<QuoteItem>();
            // v_sh600036="51~名称~代码~现价~昨收~今开~成交量~...~涨跌额~涨跌幅~最高~最低~...~成交额万~"
            foreach (var segment in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = segment.IndexOf("=\"", StringComparison.Ordinal);
                if (eq < 0) continue;
                string payload = segment[(eq + 2)..].Trim().Trim('"');
                var f = payload.Split('~');
                if (f.Length < 35) continue;

                if (!TryParseDec(f[3], out var price) || price <= 0) continue;
                TryParseDec(f.Length > 32 ? f[32] : "0", out var pct);
                TryParseDec(f.Length > 4 ? f[4] : "0", out var preClose);
                TryParseDec(f.Length > 5 ? f[5] : "0", out var open);
                TryParseDec(f.Length > 33 ? f[33] : "0", out var high);
                TryParseDec(f.Length > 34 ? f[34] : "0", out var low);
                TryParseDec(f.Length > 6 ? f[6] : "0", out var volume);
                // 成交额：腾讯为万元，转成元与东财字段对齐
                TryParseDec(f.Length > 37 ? f[37] : "0", out var turnoverWan);
                TryParseDec(f.Length > 38 ? f[38] : "0", out var turnoverRate);
                TryParseDec(f.Length > 49 ? f[49] : "0", out var volumeRatio);
                TryParseDec(f.Length > 80 ? f[80] : "0", out var speed);

                string code = f[2];
                string head = segment[..eq]; // v_sh600036
                int market = 1;
                if (head.Contains("sz", StringComparison.OrdinalIgnoreCase)) market = 0;
                else if (head.Contains("hk", StringComparison.OrdinalIgnoreCase)) market = 116;

                result.Add(new QuoteItem
                {
                    F2 = price,
                    F3 = pct,
                    F5 = volume,
                    F6 = turnoverWan * 10000m,
                    F8 = turnoverRate,
                    F10 = volumeRatio,
                    F12 = code,
                    F13 = market,
                    F14 = f[1],
                    F15 = high,
                    F16 = low,
                    F17 = preClose,
                    F18 = open,
                    F22 = speed
                });
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    private static bool TryParseDec(string s, out decimal value)
        => decimal.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    // ── Intraday（腾讯分时）──────────────────────────

    public async Task<IntradaySeries?> GetIntradayAsync(string internalCode)
    {
        // 场外基金无分时；场内 ETF/LOF 归一化为行情代码后走腾讯分时
        if (IsFundCode(internalCode) && !StockItem.IsExchangeFundCode(internalCode)) return null;
        return await GetTencentIntradayAsync(StockItem.ToExchangeCode(internalCode));
    }

    private async Task<IntradaySeries?> GetTencentIntradayAsync(string internalCode)
    {
        // https://web.ifzq.gtimg.cn/appstock/app/minute/query?code=sh600036
        string code = $"{internalCode[..2].ToLowerInvariant()}{internalCode[2..]}";
        string url = $"https://web.ifzq.gtimg.cn/appstock/app/minute/query?code={code}";

        try
        {
            string json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return null;

            if (!data.TryGetProperty(code, out var stock) && !data.TryGetProperty(code.ToUpperInvariant(), out stock))
            {
                stock = default;
                foreach (var p in data.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.Object) { stock = p.Value; break; }
                }
                if (stock.ValueKind != JsonValueKind.Object) return null;
            }

            if (!stock.TryGetProperty("data", out var inner) || inner.ValueKind != JsonValueKind.Object)
                return null;
            if (!inner.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return null;

            var prices = new List<decimal>();
            var volumes = new List<decimal>();
            var amounts = new List<decimal>();

            foreach (var item in arr.EnumerateArray())
            {
                // "0930 39.00 100 3900000" → 时间 现价 成交量 成交额
                string? line = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (!TryParseDec(parts[1], out var price) || price <= 0) continue;
                prices.Add(price);
                TryParseDec(parts.Length > 2 ? parts[2] : "0", out var vol);
                TryParseDec(parts.Length > 3 ? parts[3] : "0", out var amt);
                volumes.Add(vol);
                amounts.Add(amt);
            }

            if (prices.Count < 2) return null;

            // A股腾讯成交量为「手」(×100)；港股一般为股，不能再乘 100
            bool isHk = internalCode.StartsWith("HK", StringComparison.OrdinalIgnoreCase);
            var volumesForVwap = isHk
                ? volumes
                : volumes.Select(v => v * 100m).ToList();

            var avgs = ComputeVwap(prices, volumesForVwap, amounts);
            avgs = SanitizeAvgAgainstPrice(prices, avgs);

            return new IntradaySeries
            {
                Prices = prices,
                AvgPrices = avgs
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>若均价相对现价偏差过大（单位搞错），回退为价格简单累计均价</summary>
    private static List<decimal> SanitizeAvgAgainstPrice(List<decimal> prices, List<decimal> avgs)
    {
        if (prices.Count == 0 || avgs.Count == 0) return avgs;
        decimal lastPrice = prices[^1];
        decimal lastAvg = avgs[Math.Min(avgs.Count, prices.Count) - 1];
        if (lastPrice <= 0) return avgs;

        // 正常均价应与现价同量级；0.27 vs 26.7 或 3911 vs 39 都视为单位错误
        decimal ratio = lastAvg / lastPrice;
        if (ratio >= 0.5m && ratio <= 2.0m)
            return avgs;

        var fallback = new List<decimal>(prices.Count);
        decimal sum = 0;
        for (int i = 0; i < prices.Count; i++)
        {
            sum += prices[i];
            fallback.Add(sum / (i + 1));
        }
        return fallback;
    }

    /// <summary>累计成交额/累计成交量 → 今日均价；无量额时退化为简单均价</summary>
    private static List<decimal> ComputeVwap(List<decimal> prices, List<decimal> volumes, List<decimal> amounts)
    {
        var avgs = new List<decimal>(prices.Count);
        decimal cumVol = 0, cumAmt = 0, cumPrice = 0;
        for (int i = 0; i < prices.Count; i++)
        {
            decimal vol = i < volumes.Count ? volumes[i] : 0;
            decimal amt = i < amounts.Count ? amounts[i] : 0;
            cumPrice += prices[i];

            if (amt > 0 && vol > 0)
            {
                cumAmt += amt;
                cumVol += vol;
                avgs.Add(cumVol > 0 ? cumAmt / cumVol : prices[i]);
            }
            else if (vol > 0)
            {
                cumAmt += prices[i] * vol;
                cumVol += vol;
                avgs.Add(cumVol > 0 ? cumAmt / cumVol : prices[i]);
            }
            else
            {
                avgs.Add(cumPrice / (i + 1));
            }
        }
        return avgs;
    }

    // ── Search ───────────────────────────────────────

    public async Task<List<SearchItem>> SearchAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return [];

        string url = $"https://searchapi.eastmoney.com/api/suggest/get" +
                     $"?input={Uri.EscapeDataString(keyword)}" +
                     $"&type=14" +
                     $"&token=D43BF722C8E33BDC906FB84D85E326E8" +
                     $"&count=10";

        try
        {
            var response = await _http.GetFromJsonAsync<SearchResponse>(url, JsonOpts);
            return response?.QuotationCodeTable?.Data ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ── 天天基金搜索 ─────────────────────────────────

    public async Task<List<SearchItem>> SearchFundsAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return [];

        string url = "https://fundsuggest.eastmoney.com/FundSearch/api/FundSearchAPI.ashx" +
                     $"?m=1&key={Uri.EscapeDataString(keyword)}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://fund.eastmoney.com/");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return [];

            var response = await resp.Content.ReadFromJsonAsync<FundSearchResponse>(JsonOpts);
            return (response?.Datas ?? [])
                .Where(d => d.Category == 700 && !string.IsNullOrEmpty(d.Code) && !string.IsNullOrEmpty(d.Name))
                .Take(10)
                .Select(d => new SearchItem
                {
                    Code = d.Code,
                    Name = d.Name,
                    MarketType = "FD",
                    SecurityTypeName = d.CategoryDesc ?? "基金"
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // ── 天天基金实时估值 ─────────────────────────────

    public async Task<List<QuoteItem>> GetFundValuationsAsync(IReadOnlyList<string> internalCodes)
    {
        var codes = internalCodes
            .Select(c => IsFundCode(c) ? c[2..] : c)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0) return [];

        string query = $"FCODES={Uri.EscapeDataString(string.Join(",", codes))}" +
                       "&FIELDS=FCODE,SHORTNAME,GSZZL,GZTIME,GSZ,NAV,PDATE";
        string[] urls =
        [
            $"https://fundcomapi.tiantianfunds.com/mm/newCore/FundValuationLast?{query}",
            $"https://fundcomapi.eastmoney.com/mm/newCore/FundValuationLast?{query}"
        ];

        foreach (var url in urls)
        {
            var list = await FetchFundValuationAsync(url);
            if (list.Count > 0) return list;
        }

        return [];
    }

    private async Task<List<QuoteItem>> FetchFundValuationAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://fund.eastmoney.com/");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return [];

            var response = await resp.Content.ReadFromJsonAsync<FundValuationResponse>(JsonOpts);
            if (response?.Data == null || response.Data.Count == 0) return [];

            var result = new List<QuoteItem>(response.Data.Count);
            foreach (var item in response.Data)
            {
                if (string.IsNullOrEmpty(item.FCODE)) continue;
                decimal price = item.GSZ > 0 ? item.GSZ : item.NAV;
                decimal pct = item.GSZ > 0 ? item.GSZZL : 0;
                result.Add(new QuoteItem
                {
                    F2 = price,
                    F3 = pct,
                    F12 = item.FCODE,
                    F14 = item.SHORTNAME,
                    F17 = item.NAV
                });
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    // ── K-line (Tencent API) ──────────────────────────

    private static string TencentPrefix(string internalCode) => internalCode[..2].ToLowerInvariant();

    public async Task<List<KlineItem>> GetKlineAsync(string internalCode, int period = 5, int count = 160)
    {
        if (IsFundCode(internalCode))
        {
            // 场内 ETF/LOF 归一化为行情代码后仍取 K 线；场外基金没有 K 线
            if (!StockItem.IsExchangeFundCode(internalCode)) return [];
            internalCode = StockItem.ToExchangeCode(internalCode);
        }
        // period: 5=m5, 15=m15, 30=m30, 60=m60, 101=day, 102=week
        string prefix = TencentPrefix(internalCode);
        string code = prefix + internalCode[2..];
        string url;

        if (period is 5 or 15 or 30 or 60)
        {
            string p = $"m{period}";
            url = $"https://ifzq.gtimg.cn/appstock/app/kline/mkline?param={code},{p},,{count}";
        }
        else
        {
            string p = period == 101 ? "day" : "week";
            url = $"https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param={code},{p},,,{count},qfq";
        }

        try
        {
            string json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return [];

            // Find the stock entry (skip qt, prec, version)
            JsonElement stockEntry = default;
            foreach (var prop in data.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    stockEntry = prop.Value;
                    break;
                }
            }
            if (stockEntry.ValueKind != JsonValueKind.Object) return [];

            // Find the kline array (first array-valued property)
            JsonElement klineArray = default;
            foreach (var prop in stockEntry.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    klineArray = prop.Value;
                    break;
                }
            }
            if (klineArray.ValueKind != JsonValueKind.Array) return [];

            var result = new List<KlineItem>(klineArray.GetArrayLength());
            foreach (var item in klineArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array) continue;
                if (KlineItem.TryFromJsonArray(item, out var kline) && kline != null)
                    result.Add(kline);
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    public void Dispose() => _http.Dispose();
}
