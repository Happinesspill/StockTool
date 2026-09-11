using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockTool.Core.Models;

public class QuoteBatchResponse
{
    public QuoteDataWrapper? Data { get; set; }
}

public class QuoteDataWrapper
{
    public int Total { get; set; }

    [JsonConverter(typeof(SingleOrArrayConverter<QuoteItem>))]
    public List<QuoteItem>? Diff { get; set; }
}

public class QuoteItem
{
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F2 { get; set; }  // current price
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F3 { get; set; }  // change percent
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F4 { get; set; }  // change amount
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F5 { get; set; }  // volume (shares)
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F6 { get; set; }  // turnover (amount)
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F8 { get; set; }  // turnover rate
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F10 { get; set; } // volume ratio
    public string? F12 { get; set; } // code
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int F13 { get; set; }     // market
    public string? F14 { get; set; } // name
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F15 { get; set; } // high
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F16 { get; set; } // low
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F17 { get; set; } // yesterday close
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F18 { get; set; } // open
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F20 { get; set; } // total market value
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F21 { get; set; } // float market value
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal F22 { get; set; } // speed
}

public class IntradayResponse
{
    public IntradayDataWrapper? Data { get; set; }
}

public class IntradayDataWrapper
{
    public List<string>? Trends { get; set; }
}

public class SearchResponse
{
    public SearchTable? QuotationCodeTable { get; set; }
}

public class SearchTable
{
    public List<SearchItem>? Data { get; set; }
}

public class SearchItem
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? MarketType { get; set; }
    public string? SecurityTypeName { get; set; }
}

public class FundSearchResponse
{
    public List<FundSearchItem>? Datas { get; set; }
}

public class FundSearchItem
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public int Category { get; set; }
    public string? CategoryDesc { get; set; }
}

public class FundValuationResponse
{
    public List<FundValuationItem>? Data { get; set; }
    public bool Success { get; set; }
}

public class FundValuationItem
{
    public string? FCODE { get; set; }
    public string? SHORTNAME { get; set; }
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal GSZ { get; set; }
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal GSZZL { get; set; }
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal NAV { get; set; }
    public string? GZTIME { get; set; }
    public string? PDATE { get; set; }
}

// ── K-line ──────────────────────────────────────

public class KlineResponse
{
    public KlineDataWrapper? Data { get; set; }
}

public class KlineDataWrapper
{
    public List<string>? Klines { get; set; }
}

public class KlineItem
{
    public DateTime Date { get; set; }
    public decimal Open { get; set; }
    public decimal Close { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Volume { get; set; }

    public static bool TryFromJsonArray(JsonElement arr, out KlineItem? item)
    {
        item = null;
        if (arr.GetArrayLength() < 6) return false;

        string dateStr = arr[0].GetString() ?? "";
        if (!TryParseDate(dateStr, out var date)) return false;

        if (!TryGetDecimal(arr[1], out var open)) return false;
        if (!TryGetDecimal(arr[2], out var close)) return false;
        if (!TryGetDecimal(arr[3], out var high)) return false;
        if (!TryGetDecimal(arr[4], out var low)) return false;
        if (!TryGetDecimal(arr[5], out var volume)) return false;

        item = new KlineItem { Date = date, Open = open, Close = close, High = high, Low = low, Volume = volume };
        return true;
    }

    private static bool TryParseDate(string s, out DateTime date)
    {
        // Daily format: "2026-07-24"
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        // Minute format: "202607241050"
        if (s.Length == 12 && DateTime.TryParseExact(s, "yyyyMMddHHmm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        return false;
    }

    private static bool TryGetDecimal(JsonElement elem, out decimal value)
    {
        if (elem.ValueKind == JsonValueKind.String)
            return decimal.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        if (elem.ValueKind == JsonValueKind.Number)
        {
            value = elem.GetDecimal();
            return true;
        }
        value = 0;
        return false;
    }
}
