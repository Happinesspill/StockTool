using System.Globalization;
using System.Text.Json;

namespace StockTool.Core.Models;

public class QuoteBatchResponse
{
    public QuoteDataWrapper? Data { get; set; }
}

public class QuoteDataWrapper
{
    public int Total { get; set; }
    public List<QuoteItem>? Diff { get; set; }
}

public class QuoteItem
{
    public decimal F2 { get; set; }  // current price
    public decimal F3 { get; set; }  // change percent
    public decimal F4 { get; set; }  // change amount
    public decimal F5 { get; set; }  // volume (shares)
    public decimal F6 { get; set; }  // turnover (amount)
    public string? F12 { get; set; } // code
    public int F13 { get; set; }     // market
    public string? F14 { get; set; } // name
    public decimal F15 { get; set; } // high
    public decimal F16 { get; set; } // low
    public decimal F17 { get; set; } // yesterday close
    public decimal F18 { get; set; } // open
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
