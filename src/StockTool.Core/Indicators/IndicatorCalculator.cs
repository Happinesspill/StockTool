using StockTool.Core.Models;

namespace StockTool.Core.Indicators;

public static class IndicatorCalculator
{
    public sealed class BollResult
    {
        public decimal?[] Mid { get; init; } = [];
        public decimal?[] Upper { get; init; } = [];
        public decimal?[] Lower { get; init; } = [];
    }

    public sealed class MacdResult
    {
        public decimal?[] Dif { get; init; } = [];
        public decimal?[] Dea { get; init; } = [];
        public decimal?[] Hist { get; init; } = [];
    }

    public sealed class KdjResult
    {
        public decimal?[] K { get; init; } = [];
        public decimal?[] D { get; init; } = [];
        public decimal?[] J { get; init; } = [];
    }

    public sealed class RsiResult
    {
        public decimal?[] Rsi1 { get; init; } = [];
        public decimal?[] Rsi2 { get; init; } = [];
        public decimal?[] Rsi3 { get; init; } = [];
    }

    /// <summary>买卖点信号：K 线索引 + 方向 + 触发原因</summary>
    public sealed class TradeSignal
    {
        public int Index { get; init; }
        public bool IsBuy { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    public static BollResult Boll(IReadOnlyList<KlineItem> data, int period = 20, double mult = 2.0)
    {
        int n = data.Count;
        var mid = new decimal?[n];
        var upper = new decimal?[n];
        var lower = new decimal?[n];
        if (n < period) return new BollResult { Mid = mid, Upper = upper, Lower = lower };

        for (int i = period - 1; i < n; i++)
        {
            decimal sum = 0;
            for (int j = i - period + 1; j <= i; j++)
                sum += data[j].Close;
            decimal ma = sum / period;

            double variance = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                double d = (double)(data[j].Close - ma);
                variance += d * d;
            }
            double std = Math.Sqrt(variance / period);
            decimal band = (decimal)(std * mult);

            mid[i] = ma;
            upper[i] = ma + band;
            lower[i] = ma - band;
        }

        return new BollResult { Mid = mid, Upper = upper, Lower = lower };
    }

    public static MacdResult Macd(IReadOnlyList<KlineItem> data, int fast = 12, int slow = 26, int signal = 9)
    {
        int n = data.Count;
        var dif = new decimal?[n];
        var dea = new decimal?[n];
        var hist = new decimal?[n];
        if (n == 0) return new MacdResult { Dif = dif, Dea = dea, Hist = hist };

        var closes = data.Select(d => d.Close).ToArray();
        var emaFast = Ema(closes, fast);
        var emaSlow = Ema(closes, slow);

        var difVals = new decimal?[n];
        for (int i = 0; i < n; i++)
        {
            if (emaFast[i].HasValue && emaSlow[i].HasValue)
                difVals[i] = emaFast[i]!.Value - emaSlow[i]!.Value;
        }

        var deaVals = EmaNullable(difVals, signal);
        for (int i = 0; i < n; i++)
        {
            dif[i] = difVals[i];
            dea[i] = deaVals[i];
            if (difVals[i].HasValue && deaVals[i].HasValue)
                hist[i] = (difVals[i]!.Value - deaVals[i]!.Value) * 2m;
        }

        return new MacdResult { Dif = dif, Dea = dea, Hist = hist };
    }

    public static KdjResult Kdj(IReadOnlyList<KlineItem> data, int period = 9, int kSmooth = 3, int dSmooth = 3)
    {
        int n = data.Count;
        var kArr = new decimal?[n];
        var dArr = new decimal?[n];
        var jArr = new decimal?[n];
        if (n < period) return new KdjResult { K = kArr, D = dArr, J = jArr };

        decimal k = 50, d = 50;
        for (int i = period - 1; i < n; i++)
        {
            decimal high = data[i].High;
            decimal low = data[i].Low;
            for (int t = i - period + 1; t < i; t++)
            {
                if (data[t].High > high) high = data[t].High;
                if (data[t].Low < low) low = data[t].Low;
            }

            decimal rsv = high == low ? 50 : (data[i].Close - low) / (high - low) * 100m;
            k = (k * (kSmooth - 1) + rsv) / kSmooth;
            d = (d * (dSmooth - 1) + k) / dSmooth;
            decimal jVal = 3 * k - 2 * d;

            kArr[i] = k;
            dArr[i] = d;
            jArr[i] = jVal;
        }

        return new KdjResult { K = kArr, D = dArr, J = jArr };
    }

    public static RsiResult Rsi(IReadOnlyList<KlineItem> data, int p1 = 6, int p2 = 12, int p3 = 24)
    {
        return new RsiResult
        {
            Rsi1 = RsiSingle(data, p1),
            Rsi2 = RsiSingle(data, p2),
            Rsi3 = RsiSingle(data, p3)
        };
    }

    /// <summary>
    /// MACD+KDJ 共振买卖点：买点=MACD金叉且KDJ低位金叉，卖点=MACD死叉且KDJ高位死叉。
    /// window 允许两指标在若干根K线内先后出现；minGap 为相邻信号最小间隔，避免重复标记。
    /// </summary>
    public static List<TradeSignal> MacdKdjSignals(IReadOnlyList<KlineItem> data,
        int window = 3, decimal kdjLow = 30m, decimal kdjHigh = 70m, int minGap = 5)
    {
        var signals = new List<TradeSignal>();
        int n = data.Count;
        if (n < 2) return signals;

        var macd = Macd(data);
        var kdj = Kdj(data);

        var macdGolden = new bool[n];
        var macdDead = new bool[n];
        var kdjGolden = new bool[n];
        var kdjDead = new bool[n];

        for (int i = 1; i < n; i++)
        {
            // MACD 金叉 / 死叉（DIF 上穿 / 下穿 DEA）
            var dif0 = macd.Dif[i - 1];
            var dea0 = macd.Dea[i - 1];
            var dif1 = macd.Dif[i];
            var dea1 = macd.Dea[i];
            if (dif0.HasValue && dea0.HasValue && dif1.HasValue && dea1.HasValue)
            {
                if (dif0.Value <= dea0.Value && dif1.Value > dea1.Value) macdGolden[i] = true;
                if (dif0.Value >= dea0.Value && dif1.Value < dea1.Value) macdDead[i] = true;
            }

            // KDJ 低位金叉 / 高位死叉
            var k0 = kdj.K[i - 1];
            var d0 = kdj.D[i - 1];
            var k1 = kdj.K[i];
            var d1 = kdj.D[i];
            if (k0.HasValue && d0.HasValue && k1.HasValue && d1.HasValue)
            {
                if (k0.Value <= d0.Value && k1.Value > d1.Value && k1.Value < kdjLow) kdjGolden[i] = true;
                if (k0.Value >= d0.Value && k1.Value < d1.Value && k1.Value > kdjHigh) kdjDead[i] = true;
            }
        }

        // 用半量级初值，避免 index - lastIdx 溢出
        int lastIdx = int.MinValue / 2;
        for (int i = 1; i < n; i++)
        {
            if (kdjGolden[i] && NearestMark(macdGolden, i, window, out int buyIdx))
                Add(Math.Max(i, buyIdx), true, "MACD金叉+KDJ低位金叉");
            else if (kdjDead[i] && NearestMark(macdDead, i, window, out int sellIdx))
                Add(Math.Max(i, sellIdx), false, "MACD死叉+KDJ高位死叉");
        }

        return signals;

        // 同向信号间隔不足 minGap 时丢弃
        void Add(int index, bool isBuy, string reason)
        {
            if (index - lastIdx < minGap) return;
            lastIdx = index;
            signals.Add(new TradeSignal { Index = index, IsBuy = isBuy, Reason = reason });
        }
    }

    // 在 index 前后 window 根内查找最近的标记位
    private static bool NearestMark(bool[] marks, int index, int window, out int found)
    {
        found = -1;
        for (int d = 0; d <= window; d++)
        {
            if (index - d >= 0 && marks[index - d]) { found = index - d; return true; }
            if (index + d < marks.Length && marks[index + d]) { found = index + d; return true; }
        }
        return false;
    }

    private static decimal?[] RsiSingle(IReadOnlyList<KlineItem> data, int period)
    {
        int n = data.Count;
        var result = new decimal?[n];
        if (n <= period) return result;

        decimal avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            decimal change = data[i].Close - data[i - 1].Close;
            if (change >= 0) avgGain += change;
            else avgLoss -= change;
        }
        avgGain /= period;
        avgLoss /= period;
        result[period] = avgLoss == 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);

        for (int i = period + 1; i < n; i++)
        {
            decimal change = data[i].Close - data[i - 1].Close;
            decimal gain = change > 0 ? change : 0;
            decimal loss = change < 0 ? -change : 0;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            result[i] = avgLoss == 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
        }

        return result;
    }

    private static decimal?[] Ema(decimal[] values, int period)
    {
        int n = values.Length;
        var result = new decimal?[n];
        if (n < period) return result;

        decimal sum = 0;
        for (int i = 0; i < period; i++)
            sum += values[i];
        result[period - 1] = sum / period;

        decimal k = 2m / (period + 1);
        for (int i = period; i < n; i++)
            result[i] = values[i] * k + result[i - 1]!.Value * (1 - k);

        return result;
    }

    private static decimal?[] EmaNullable(decimal?[] values, int period)
    {
        int n = values.Length;
        var result = new decimal?[n];

        int start = -1;
        for (int i = 0; i < n; i++)
        {
            if (values[i].HasValue) { start = i; break; }
        }
        if (start < 0 || start + period > n) return result;

        decimal sum = 0;
        for (int i = start; i < start + period; i++)
        {
            if (!values[i].HasValue) return result;
            sum += values[i]!.Value;
        }
        result[start + period - 1] = sum / period;

        decimal k = 2m / (period + 1);
        for (int i = start + period; i < n; i++)
        {
            if (!values[i].HasValue || !result[i - 1].HasValue) continue;
            result[i] = values[i]!.Value * k + result[i - 1]!.Value * (1 - k);
        }

        return result;
    }
}
