namespace StockTool.Services;

public static class TradingHours
{
    // A-share trading sessions (Beijing time)
    private static readonly TimeSpan AmOpenA = new(9, 30, 0);
    private static readonly TimeSpan AmCloseA = new(11, 30, 0);
    private static readonly TimeSpan PmOpenA = new(13, 0, 0);
    private static readonly TimeSpan PmCloseA = new(15, 0, 0);

    // HK trading sessions (Beijing time, same as HKT)
    private static readonly TimeSpan AmOpenHk = new(9, 30, 0);
    private static readonly TimeSpan AmCloseHk = new(12, 0, 0);
    private static readonly TimeSpan PmOpenHk = new(13, 0, 0);
    private static readonly TimeSpan PmCloseHk = new(16, 0, 0);

    public static bool IsTradingTime(string market)
    {
        var now = DateTime.Now;
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        var t = now.TimeOfDay;

        if (market is "沪A" or "深A" or "ETF" or "基金")
            return (t >= AmOpenA && t <= AmCloseA) || (t >= PmOpenA && t <= PmCloseA);

        if (market == "港股")
            return (t >= AmOpenHk && t <= AmCloseHk) || (t >= PmOpenHk && t <= PmCloseHk);

        return false;
    }

    /// <summary>Returns true if ANY market in the list is currently trading.</summary>
    public static bool AnyTrading(IEnumerable<string> markets)
        => markets.Any(IsTradingTime);

    /// <summary>Returns the recommended refresh interval in seconds based on trading status.</summary>
    public static int GetQuoteInterval(int configuredInterval, bool isAnyTrading)
        => isAnyTrading ? configuredInterval : Math.Max(configuredInterval * 5, 10);

    public static int GetIntradayInterval(bool isAnyTrading)
        => isAnyTrading ? 60 : 300;
}
