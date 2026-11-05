using System.Net.Http.Json;
using System.Text.Json;
using StockTool.Core.Models;

namespace StockTool.Data;

public class EastMoneyClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>A 股报价源：eastmoney | tencent | sina</summary>
    public string PreferredQuoteSource { get; set; } = "eastmoney";

    public EastMoneyClient()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        _http = new HttpClient();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        // Referer 按接口分别设置，全局写腾讯 Referer 会导致东财 trends2 被断开
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
            "BJ" => 0, // 北交所在东财仍为市场 0
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
            string raw = parts[1];
            string prefix = market switch
            {
                1 => "SH",
                // 北交所代码多为 4/8/9 开头（如 899050、430047）
                0 => IsBeijingExchangeCode(raw) ? "BJ" : "SZ",
                116 => "HK",
                _ => "SH"
            };
            return $"{prefix}{raw}";
        }

        // 全球指数等：100.HSI → IX100.HSI；腾讯道指代码 DJI 归一为 DJIA
        string sym = parts[1].TrimStart('.');
        if (string.Equals(sym, "DJI", StringComparison.OrdinalIgnoreCase))
            sym = "DJIA";
        return $"IX{parts[0]}.{sym}";
    }

    /// <summary>北交所证券/指数代码（市场号与深市同为 0）</summary>
    public static bool IsBeijingExchangeCode(string code) =>
        !string.IsNullOrEmpty(code)
        && (code.StartsWith('4') || code.StartsWith('8') || code.StartsWith('9'));

    /// <summary>全球指数等：东财 push2 优先，失败回退腾讯（usNDX / hkHSI）。</summary>
    public async Task<List<QuoteItem>> GetPush2QuotesAsync(IReadOnlyList<string> internalCodes)
    {
        if (internalCodes.Count == 0) return [];
        try
        {
            var em = await GetEastMoneyQuotesAsync(internalCodes);
            if (em.Count > 0 && em.Any(r => r.F2 > 0))
                return em;
        }
        catch
        {
            // fall through
        }

        try
        {
            return await GetTencentQuotesAsync(internalCodes);
        }
        catch
        {
            return [];
        }
    }

    public static bool IsFundCode(string internalCode)
        => internalCode.StartsWith("FD", StringComparison.OrdinalIgnoreCase);

    public static string ToFundInternalCode(string fundCode)
        => $"FD{fundCode.Trim()}";

    public static string MarketLabelFromPrefix(string prefix) => prefix switch
    {
        "SH" => "沪A",
        "SZ" => "深A",
        "BJ" => "北交所",
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

        // 按用户选择的行情源优先，失败再回退其余源
        var order = PreferredQuoteSource?.Trim().ToLowerInvariant() switch
        {
            "tencent" => new Func<IReadOnlyList<string>, Task<List<QuoteItem>>>[]
            {
                GetTencentQuotesAsync, GetSinaQuotesAsync, GetEastMoneyQuotesAsync
            },
            "sina" =>
            [
                GetSinaQuotesAsync, GetTencentQuotesAsync, GetEastMoneyQuotesAsync
            ],
            _ =>
            [
                GetEastMoneyQuotesAsync, GetTencentQuotesAsync, GetSinaQuotesAsync
            ]
        };

        foreach (var fetch in order)
        {
            try
            {
                var result = await fetch(internalCodes);
                if (result.Count > 0 && result.Any(r => r.F2 > 0))
                    return result;
            }
            catch
            {
                // try next
            }
        }

        return [];
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
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://finance.sina.com.cn/");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return [];
            var bytes = await resp.Content.ReadAsByteArrayAsync();
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
                    : varName.Contains("bj", StringComparison.OrdinalIgnoreCase) ? 0
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
        // https://qt.gtimg.cn/q=sh600036,hk01810,bj899050,usNDX
        var symbols = internalCodes
            .Select(ToTencentSymbol)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (symbols.Count == 0) return [];
        string url = $"https://qt.gtimg.cn/q={string.Join(",", symbols)}";

        try
        {
            // 腾讯返回 GBK
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://finance.qq.com/");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return [];
            var bytes = await resp.Content.ReadAsByteArrayAsync();
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

                string code = (f[2] ?? "").TrimStart('.');
                string head = segment[..eq]; // v_sh600036 / v_usNDX / v_bj899050
                int market = ResolveTencentMarket(head, code);

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

    /// <summary>内部代码 → 腾讯行情符号</summary>
    private static string ToTencentSymbol(string internalCode)
    {
        if (string.IsNullOrWhiteSpace(internalCode) || internalCode.Length < 3)
            return "";

        if (internalCode.StartsWith("IX", StringComparison.OrdinalIgnoreCase) && internalCode.Contains('.'))
        {
            string sym = internalCode[(internalCode.LastIndexOf('.') + 1)..].Trim().ToUpperInvariant();
            return sym switch
            {
                "NDX" or "IXIC" => "usNDX",
                "SPX" => "usSPX",
                "DJIA" or "DJI" => "usDJI",
                "HSI" => "hkHSI",
                _ => $"us{sym}"
            };
        }

        string prefix = internalCode[..2].ToLowerInvariant();
        string code = internalCode[2..];
        return prefix switch
        {
            "sh" or "sz" or "hk" or "bj" => prefix + code,
            "fd" => "",
            _ => prefix + code
        };
    }

    private static int ResolveTencentMarket(string head, string code)
    {
        if (head.Contains("us", StringComparison.OrdinalIgnoreCase)) return 100;
        if (head.Contains("hk", StringComparison.OrdinalIgnoreCase))
            return string.Equals(code, "HSI", StringComparison.OrdinalIgnoreCase) ? 100 : 116;
        if (head.Contains("bj", StringComparison.OrdinalIgnoreCase)) return 0;
        if (head.Contains("sz", StringComparison.OrdinalIgnoreCase)) return 0;
        return 1; // sh
    }

    private static bool TryParseDec(string s, out decimal value)
        => decimal.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    // ── 汇率（港股通参考汇率，对齐券商/同花顺持仓折算）──────────────────────────

    private decimal? _cachedHkdCnyRate;
    private DateTime _cachedHkdCnyAt = DateTime.MinValue;

    private static readonly HttpClient FxHttp = CreateFxHttp();

    private static HttpClient CreateFxHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        return http;
    }

    /// <summary>
    /// 1 港币折合人民币。优先上交所港股通参考汇率买卖中间价（与同花顺市值一致），
    /// 失败再回退东财港币人民币中间价。
    /// </summary>
    public async Task<decimal?> GetHkdCnyRateAsync()
    {
        if (_cachedHkdCnyRate is > 0
            && DateTime.Now - _cachedHkdCnyAt < TimeSpan.FromMinutes(10))
            return _cachedHkdCnyRate;

        decimal? rate = await FetchSseHkConnectMidRateAsync();
        rate ??= await FetchEastMoneyHkdCnyMidAsync();
        if (rate is not > 0) return _cachedHkdCnyRate;

        _cachedHkdCnyRate = rate;
        _cachedHkdCnyAt = DateTime.Now;
        return rate;
    }

    /// <summary>上交所港股通参考汇率：(买入价+卖出价)/2</summary>
    private static async Task<decimal?> FetchSseHkConnectMidRateAsync()
    {
        // https://www.sse.com.cn/services/hkexsc/disclo/ratios/
        string today = DateTime.Now.ToString("yyyyMMdd");
        string begin = DateTime.Now.AddDays(-14).ToString("yyyyMMdd");
        string url = "https://query.sse.com.cn/commonSoaQuery.do"
                     + "?sqlId=FW_HGT_GGTHL"
                     + $"&updateDate={begin}&updateDateEnd={today}"
                     + "&isPagination=true&pageHelp.pageSize=5&pageHelp.pageNo=1";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://www.sse.com.cn/");
            using var resp = await FxHttp.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json) || json.Length < 20) return null;

            using var doc = JsonDocument.Parse(json);
            if (!TryGetSseRateArray(doc.RootElement, out var arr)) return null;

            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!TryReadSsePrice(item, "buyPrice", out var buy)) continue;
                if (!TryReadSsePrice(item, "sellPrice", out var sell)) continue;
                if (buy <= 0 || sell <= 0) continue;

                decimal mid = (buy + sell) / 2m;
                if (mid > 0 && mid < 2m) return mid;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetSseRateArray(JsonElement root, out JsonElement arr)
    {
        arr = default;
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array
            && result.GetArrayLength() > 0)
        {
            arr = result;
            return true;
        }
        if (root.TryGetProperty("pageHelp", out var page)
            && page.ValueKind == JsonValueKind.Object
            && page.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0)
        {
            arr = data;
            return true;
        }
        return false;
    }

    private static bool TryReadSsePrice(JsonElement item, string name, out decimal value)
    {
        value = 0;
        if (!item.TryGetProperty(name, out var el)) return false;
        return TryParseDec((el.GetString() ?? el.ToString()).Trim(), out value) && value > 0;
    }

    private static async Task<decimal?> FetchEastMoneyHkdCnyMidAsync()
    {
        // https://quote.eastmoney.com/cnyrate/HKDCNYC.html
        string url = "https://push2.eastmoney.com/api/qt/stock/get"
                     + "?secid=120.HKDCNYC&fields=f43,f60&fltt=2";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://quote.eastmoney.com/");
            using var resp = await FxHttp.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
                return null;

            decimal rate = 0;
            if (data.TryGetProperty("f43", out var f43))
                rate = ReadDecimal(f43);
            if (rate <= 0 && data.TryGetProperty("f60", out var f60))
                rate = ReadDecimal(f60);

            if (rate > 10m) rate /= 100m;
            return rate is > 0 and < 2m ? rate : null;
        }
        catch
        {
            return null;
        }
    }

    private static decimal ReadDecimal(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : 0,
        JsonValueKind.String => TryParseDec(el.GetString() ?? "", out var d) ? d : 0,
        _ => 0
    };

    // ── Intraday（腾讯 ifzq 分时优先，与 K 线同域；东财兜底）──────────────────────────

    private static readonly string[] EastMoneyTrendHosts =
    [
        "https://push2.eastmoney.com",
        "https://push2delay.eastmoney.com",
        "https://82.push2.eastmoney.com",
        "https://push2his.eastmoney.com"
    ];

    public async Task<IntradaySeries?> GetIntradayAsync(string internalCode)
    {
        // 场外基金无分时；场内 ETF/LOF 归一化为行情代码后取分时
        if (IsFundCode(internalCode) && !StockItem.IsExchangeFundCode(internalCode)) return null;
        string code = StockItem.ToExchangeCode(internalCode);

        // web.ifzq 分时常被 WAF 501；ifzq.gtimg.cn 与 5 分/日 K 同域可用
        var series = await GetTencentIntradayAsync(code);
        if (series != null) return series;

        series = await GetEastMoneyIntradayAsync(code);
        if (series != null) return series;

        // 最后用腾讯 1 分钟 K 线拼分时
        return await GetTencentMinuteKlineAsIntradayAsync(code);
    }

    private async Task<IntradaySeries?> GetTencentIntradayAsync(string internalCode)
    {
        // 注意：用 ifzq.gtimg.cn，不要用 web.ifzq.gtimg.cn（易被 WAF）
        string code = $"{internalCode[..2].ToLowerInvariant()}{internalCode[2..]}";
        string url = $"https://ifzq.gtimg.cn/appstock/app/minute/query?code={code}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://finance.qq.com/");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith('<'))
                return null;

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
            var times = new List<string>();
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

                string minute = FormatMinute(parts[0]);
                if (!IsInTradingSession(internalCode, minute)) continue;

                prices.Add(price);
                times.Add(minute);
                TryParseDec(parts.Length > 2 ? parts[2] : "0", out var vol);
                TryParseDec(parts.Length > 3 ? parts[3] : "0", out var amt);
                volumes.Add(vol);
                amounts.Add(amt);
            }

            if (prices.Count < 2) return null;

            bool isHk = internalCode.StartsWith("HK", StringComparison.OrdinalIgnoreCase);
            var volumesForVwap = isHk
                ? volumes
                : volumes.Select(v => v * 100m).ToList();

            var avgs = ComputeVwap(prices, volumesForVwap, amounts);
            avgs = SanitizeAvgAgainstPrice(prices, avgs);

            return new IntradaySeries
            {
                Prices = prices,
                AvgPrices = avgs,
                Times = times
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<IntradaySeries?> GetTencentMinuteKlineAsIntradayAsync(string internalCode)
    {
        // mkline m1：与 5 分 K 同接口，收盘价序列可作分时兜底
        try
        {
            var bars = await GetKlineAsync(internalCode, period: 1, count: 300);
            if (bars.Count < 2) return null;

            var today = DateTime.Today;
            var todayBars = bars.Where(b => b.Date.Date == today).ToList();
            if (todayBars.Count < 2)
                todayBars = bars.TakeLast(Math.Min(bars.Count, 240)).ToList();
            if (todayBars.Count < 2) return null;

            var prices = new List<decimal>(todayBars.Count);
            var times = new List<string>(todayBars.Count);
            foreach (var bar in todayBars)
            {
                if (bar.Close <= 0) continue;
                string minute = bar.Date.ToString("HH:mm");
                if (!IsInTradingSession(internalCode, minute)) continue;
                prices.Add(bar.Close);
                times.Add(minute);
            }

            if (prices.Count < 2) return null;

            var avgs = new List<decimal>(prices.Count);
            decimal sum = 0;
            for (int i = 0; i < prices.Count; i++)
            {
                sum += prices[i];
                avgs.Add(sum / (i + 1));
            }

            return new IntradaySeries
            {
                Prices = prices,
                AvgPrices = avgs,
                Times = times
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<IntradaySeries?> GetEastMoneyIntradayAsync(string internalCode)
    {
        // https://push2.eastmoney.com/api/qt/stock/trends2/get?secid=1.601336&ndays=1
        string secid;
        try
        {
            secid = ToSecId(internalCode);
        }
        catch
        {
            return null;
        }

        string path = "/api/qt/stock/trends2/get" +
                      "?fields1=f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13" +
                      "&fields2=f51,f52,f53,f54,f55,f56,f57,f58" +
                      "&ut=fa5fd1943c7b386f172d6893dbfba10b" +
                      $"&ndays=1&iscr=0&secid={Uri.EscapeDataString(secid)}";

        foreach (var host in EastMoneyTrendHosts)
        {
            try
            {
                var series = await FetchEastMoneyTrendsAsync(host + path, internalCode);
                if (series != null) return series;
            }
            catch
            {
                // try next host
            }
        }

        return null;
    }

    private async Task<IntradaySeries?> FetchEastMoneyTrendsAsync(string url, string internalCode)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Referer", "https://quote.eastmoney.com/");
        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;

        string json = await resp.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith('<'))
            return null;

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return null;
        if (!data.TryGetProperty("trends", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var prices = new List<decimal>();
        var avgs = new List<decimal>();
        var times = new List<string>();

        foreach (var item in arr.EnumerateArray())
        {
            // "2026-11-02 09:30,50.25,50.25,50.25,50.25,2965,14899125.00,50.250"
            //  → 日期时间,开,收/现价,高,低,量,额,均价
            string? line = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            if (!TryParseDec(parts[2], out var price) || price <= 0) continue;

            string minute = ExtractTrendMinute(parts[0]);
            if (!IsInTradingSession(internalCode, minute)) continue;

            prices.Add(price);
            times.Add(minute);
            if (parts.Length > 7 && TryParseDec(parts[7], out var avg) && avg > 0)
                avgs.Add(avg);
            else
                avgs.Add(price);
        }

        if (prices.Count < 2) return null;

        avgs = SanitizeAvgAgainstPrice(prices, avgs);
        return new IntradaySeries
        {
            Prices = prices,
            AvgPrices = avgs,
            Times = times
        };
    }

    /// <summary>"2026-11-02 09:30" / "09:30" → "09:30"</summary>
    private static string ExtractTrendMinute(string raw)
    {
        string s = raw.Trim();
        int space = s.LastIndexOf(' ');
        if (space >= 0 && space < s.Length - 1)
            s = s[(space + 1)..];
        return FormatMinute(s.Replace(":", ""));
    }

    /// <summary>"0930" → "09:30"；兼容 12 位时间戳，异常时原样返回</summary>
    private static string FormatMinute(string raw)
    {
        string s = raw.Trim();
        if (s.Length == 12) s = s[8..];
        if (s.Length == 4 && int.TryParse(s, out int hhmm))
            return $"{hhmm / 100:00}:{hhmm % 100:00}";
        return s;
    }

    /// <summary>时间点是否落在该市场的交易时段内（A 股 15:00、港股 16:00 收盘）</summary>
    private static bool IsInTradingSession(string internalCode, string minute)
    {
        if (!TimeSpan.TryParseExact(minute, "hh\\:mm",
                System.Globalization.CultureInfo.InvariantCulture, out var t))
            return true;   // 解析不出时间时保留，避免误删数据

        if (internalCode.StartsWith("HK", StringComparison.OrdinalIgnoreCase))
            return (t >= new TimeSpan(9, 30, 0) && t <= new TimeSpan(12, 0, 0))
                || (t >= new TimeSpan(13, 0, 0) && t <= new TimeSpan(16, 0, 0));

        return (t >= new TimeSpan(9, 30, 0) && t <= new TimeSpan(11, 30, 0))
            || (t >= new TimeSpan(13, 0, 0) && t <= new TimeSpan(15, 0, 0));
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

    // ── 天天基金实时估值（无估值时回退新浪 fu_） ─────

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

        List<QuoteItem> list = [];
        foreach (var url in urls)
        {
            list = await FetchFundValuationAsync(url);
            if (list.Count > 0) break;
        }

        var byCode = new Dictionary<string, QuoteItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            if (!string.IsNullOrEmpty(item.F12))
                byCode[item.F12] = item;
        }

        var needSina = codes
            .Where(c => !byCode.TryGetValue(c, out var q) || !q.HasEstimate)
            .ToList();
        if (needSina.Count == 0) return list;

        var sinaList = await FetchSinaFundValuationsAsync(needSina);
        foreach (var sina in sinaList)
        {
            if (string.IsNullOrEmpty(sina.F12) || !sina.HasEstimate) continue;
            if (byCode.TryGetValue(sina.F12, out var existing))
            {
                existing.F2 = sina.F2;
                existing.F3 = sina.F3;
                existing.HasEstimate = true;
                if (sina.F17 > 0) existing.F17 = sina.F17;
                if (!string.IsNullOrEmpty(sina.F14) && string.IsNullOrEmpty(existing.F14))
                    existing.F14 = sina.F14;
            }
            else
            {
                byCode[sina.F12] = sina;
                list.Add(sina);
            }
        }

        return list;
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
                bool hasEstimate = item.GSZ > 0;
                decimal price = hasEstimate ? item.GSZ : item.NAV;
                decimal pct = hasEstimate ? item.GSZZL : 0;
                result.Add(new QuoteItem
                {
                    F2 = price,
                    F3 = pct,
                    F12 = item.FCODE,
                    F14 = item.SHORTNAME,
                    F17 = item.NAV,
                    HasEstimate = hasEstimate
                });
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    // 新浪开放式基金估值：fu_代码 = 名称,时间,估值,净值,累计净值,涨跌额?,涨跌幅,日期,...
    private async Task<List<QuoteItem>> FetchSinaFundValuationsAsync(IReadOnlyList<string> fundCodes)
    {
        var result = new List<QuoteItem>();
        if (fundCodes.Count == 0) return result;

        const int batchSize = 40;
        for (int i = 0; i < fundCodes.Count; i += batchSize)
        {
            var batch = fundCodes.Skip(i).Take(batchSize).ToList();
            string list = string.Join(",", batch.Select(c => $"fu_{c}"));
            string url = $"https://hq.sinajs.cn/list={list}";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Referer", "https://finance.sina.com.cn/");
                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) continue;

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                string text;
                try { text = System.Text.Encoding.GetEncoding("GBK").GetString(bytes); }
                catch { text = System.Text.Encoding.UTF8.GetString(bytes); }

                foreach (var segment in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = segment.IndexOf('=');
                    if (eq < 0) continue;
                    string varName = segment[..eq].Trim();
                    string payload = segment[(eq + 1)..].Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(payload)) continue;

                    int fu = varName.LastIndexOf("fu_", StringComparison.OrdinalIgnoreCase);
                    if (fu < 0) continue;
                    string code = varName[(fu + 3)..].Trim();
                    if (string.IsNullOrEmpty(code)) continue;

                    var f = payload.Split(',');
                    // 名称,时间,估值,净值,累计净值,?,涨跌幅%,日期
                    if (f.Length < 7) continue;
                    if (!decimal.TryParse(f[2], System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var gsz) || gsz <= 0)
                        continue;
                    if (!decimal.TryParse(f[6], System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var gszzl))
                        gszzl = 0;
                    decimal.TryParse(f[3], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var nav);

                    result.Add(new QuoteItem
                    {
                        F2 = gsz,
                        F3 = gszzl,
                        F12 = code,
                        F14 = f[0],
                        F17 = nav,
                        HasEstimate = true
                    });
                }
            }
            catch
            {
                // 单批失败则跳过
            }
        }

        return result;
    }

    // ── 基金历史净值 ─────────────────────────────────

    public async Task<List<FundNavPoint>> GetFundNavHistoryAsync(string fundCode, int maxPages = 40)
    {
        string code = IsFundCode(fundCode) ? fundCode[2..] : fundCode;
        if (string.IsNullOrWhiteSpace(code)) return [];

        var all = new List<FundNavPoint>();
        try
        {
            for (int page = 1; page <= maxPages; page++)
            {
                string url = "https://api.fund.eastmoney.com/f10/lsjz"
                    + $"?fundCode={Uri.EscapeDataString(code)}"
                    + $"&pageIndex={page}&pageSize=20&startDate=&endDate=";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Referer", $"https://fundf10.eastmoney.com/jjjz_{code}.html");
                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) break;

                var response = await resp.Content.ReadFromJsonAsync<FundNavHistoryResponse>(JsonOpts);
                var list = response?.Data?.LSJZList;
                if (list == null || list.Count == 0) break;

                foreach (var item in list)
                {
                    if (string.IsNullOrEmpty(item.FSRQ) || item.DWJZ <= 0) continue;
                    all.Add(new FundNavPoint
                    {
                        Date = item.FSRQ,
                        Nav = item.DWJZ,
                        AccNav = item.LJJZ,
                        ChangePercent = item.JZZZL
                    });
                }

                int total = response?.TotalCount ?? 0;
                if (page * 20 >= total) break;
            }
        }
        catch
        {
            return all.OrderBy(p => p.Date).ToList();
        }

        return all
            .GroupBy(p => p.Date)
            .Select(g => g.First())
            .OrderBy(p => p.Date)
            .ToList();
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
        // period: 1=m1, 5=m5, 15=m15, 30=m30, 60=m60, 101=day, 102=week
        string prefix = TencentPrefix(internalCode);
        string code = prefix + internalCode[2..];
        string url;

        if (period is 1 or 5 or 15 or 30 or 60)
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
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://finance.qq.com/");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return [];
            string json = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith('<'))
                return [];

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
