using StockTool.Core.Models;

namespace StockTool.Core.Indicators;

/// <summary>已实施分红：除权日 + 每股派息（元，税前）</summary>
public sealed class DividendEvent
{
    public DateTime Date { get; init; }
    public decimal PerShare { get; init; }
}

/// <summary>
/// 红利股多周期 BOLL 策略（月下吸 / 龙回头）：
/// 买点 = 日线+周线在下轨，且月线下轨或回踩中轨，月线 MACD 在零轴下方，股息率达标；
/// 卖点 = 股息率由上向下跌破阈值（股价抬升导致估值偏高）。
/// </summary>
public static class DividendBollStrategy
{
    public sealed class Result
    {
        public List<IndicatorCalculator.TradeSignal> Signals { get; init; } = [];
        /// <summary>最新股息率（%）</summary>
        public decimal CurrentYield { get; init; }
        /// <summary>股息率降到卖出阈值对应的股价</summary>
        public decimal SellPrice { get; init; }
        /// <summary>图上摘要文本</summary>
        public string Summary { get; init; } = string.Empty;
    }

    public static Result Evaluate(
        IReadOnlyList<KlineItem> daily,
        IReadOnlyList<KlineItem> weekly,
        IReadOnlyList<KlineItem> monthly,
        IReadOnlyList<DividendEvent> dividends,
        decimal buyYield = 4.5m,
        decimal sellYield = 3.5m,
        decimal tolerance = 0.01m,
        int minGap = 5)
    {
        var signals = new List<IndicatorCalculator.TradeSignal>();
        if (dividends.Count == 0)
            return new Result { Signals = signals, Summary = "红利策略：无分红数据" };
        if (daily.Count < 21 || weekly.Count < 21 || monthly.Count < 21)
            return new Result { Signals = signals, Summary = "红利策略：数据不足" };

        var dBoll = IndicatorCalculator.Boll(daily);
        var wBoll = IndicatorCalculator.Boll(weekly);
        var mBoll = IndicatorCalculator.Boll(monthly);
        var mMacd = IndicatorCalculator.Macd(monthly);

        int lastBuyIdx = -1000;
        decimal? prevYield = null;
        bool buyNow = false;
        decimal currentYield = 0;

        for (int i = 0; i < daily.Count; i++)
        {
            var bar = daily[i];
            int w = LastIndexOnOrBefore(weekly, bar.Date);
            int m = LastIndexOnOrBefore(monthly, bar.Date);
            if (w < 0 || m < 0) continue;
            if (!dBoll.Lower[i].HasValue || !wBoll.Lower[w].HasValue || !mBoll.Lower[m].HasValue) continue;

            decimal ttm = TtmDividend(dividends, bar.Date);
            decimal yield = bar.Close > 0 ? ttm / bar.Close * 100m : 0;

            bool dLower = bar.Close <= dBoll.Lower[i]!.Value * (1 + tolerance);
            bool wLower = weekly[w].Close <= wBoll.Lower[w]!.Value * (1 + tolerance);
            bool mLower = monthly[m].Close <= mBoll.Lower[m]!.Value * (1 + tolerance);
            bool mMid = mBoll.Mid[m].HasValue
                        && monthly[m].Close <= mBoll.Mid[m]!.Value * (1 + tolerance);
            bool pit = mMacd.Dif[m].HasValue && mMacd.Dif[m]!.Value < 0;
            bool yieldOk = yield >= buyYield;

            // 买点：日线+周线下轨共振，月线下轨或回踩中轨，月线 MACD 零轴下方，股息率达标
            bool buyCondition = dLower && wLower && (mLower || mMid) && pit && yieldOk;
            if (i == daily.Count - 1) buyNow = buyCondition;

            if (buyCondition && i - lastBuyIdx >= minGap)
            {
                signals.Add(new IndicatorCalculator.TradeSignal
                {
                    Index = i,
                    IsBuy = true,
                    Reason = mLower ? "月下吸" : "龙回头"
                });
                lastBuyIdx = i;
            }

            // 卖点：股息率由上向下跌破卖出阈值
            if (prevYield.HasValue && prevYield.Value > sellYield && yield <= sellYield)
            {
                signals.Add(new IndicatorCalculator.TradeSignal
                {
                    Index = i,
                    IsBuy = false,
                    Reason = $"股息率{yield:F2}%"
                });
            }

            prevYield = yield;
            currentYield = yield;
        }

        var lastBar = daily[^1];
        decimal lastTtm = TtmDividend(dividends, lastBar.Date);
        decimal sellPrice = sellYield > 0 && lastTtm > 0 ? lastTtm / (sellYield / 100m) : 0;

        string summary = $"红利策略 股息率{currentYield:F2}%";
        if (sellPrice > 0) summary += $" 减仓价{sellPrice:F2}";
        if (buyNow) summary = "买点条件满足 " + summary;

        return new Result
        {
            Signals = signals,
            CurrentYield = currentYield,
            SellPrice = sellPrice,
            Summary = summary
        };
    }

    /// <summary>日线日期对应的最后一条周线/月线索引</summary>
    private static int LastIndexOnOrBefore(IReadOnlyList<KlineItem> bars, DateTime date)
    {
        for (int i = bars.Count - 1; i >= 0; i--)
        {
            if (bars[i].Date.Date <= date.Date) return i;
        }
        return -1;
    }

    /// <summary>近 12 个月已实施分红合计（元/股）</summary>
    private static decimal TtmDividend(IReadOnlyList<DividendEvent> dividends, DateTime date)
    {
        var from = date.AddYears(-1);
        decimal sum = 0;
        foreach (var d in dividends)
        {
            if (d.Date <= date && d.Date > from) sum += d.PerShare;
        }
        return sum;
    }
}
