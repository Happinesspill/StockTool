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
