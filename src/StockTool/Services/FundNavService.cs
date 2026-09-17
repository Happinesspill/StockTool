using StockTool.Core.Models;
using StockTool.Core.Services;
using StockTool.Data;

namespace StockTool.Services;

// 基金净值拉取与本地缓存
public class FundNavService
{
    private readonly EastMoneyClient _client;
    private readonly FundNavStore _store = new();

    public FundNavService(EastMoneyClient client)
    {
        _client = client;
    }

    public async Task<List<FundNavPoint>> GetHistoryAsync(string fundCode, bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            var cached = _store.Load(fundCode);
            if (cached.Count > 0)
            {
                var last = cached[^1].Date;
                if (DateTime.TryParse(last, out var lastDate)
                    && lastDate.Date >= DateTime.Today.AddDays(-3))
                    return cached;
            }
        }

        var remote = await _client.GetFundNavHistoryAsync(fundCode);
        if (remote.Count > 0)
            _store.Save(fundCode, remote);
        else
            return _store.Load(fundCode);

        return remote;
    }

    public FundNavPoint? FindNav(string fundCode, string dateYmd)
    {
        var cached = _store.Load(fundCode);
        return cached.FirstOrDefault(p =>
            string.Equals(p.Date, dateYmd, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<FundNavPoint?> EnsureNavAsync(string fundCode, string dateYmd)
    {
        var hit = FindNav(fundCode, dateYmd);
        if (hit != null) return hit;

        var history = await GetHistoryAsync(fundCode, forceRefresh: true);
        return history.FirstOrDefault(p =>
            string.Equals(p.Date, dateYmd, StringComparison.OrdinalIgnoreCase));
    }

    public static List<FundNavChartPoint> BuildChart(
        IReadOnlyList<FundNavPoint> history,
        string range,
        out decimal periodReturn)
    {
        periodReturn = 0;
        if (history.Count == 0) return [];

        DateTime? start = range switch
        {
            "1m" => DateTime.Today.AddMonths(-1),
            "3m" => DateTime.Today.AddMonths(-3),
            "6m" => DateTime.Today.AddMonths(-6),
            "1y" => DateTime.Today.AddYears(-1),
            _ => null
        };

        var filtered = history
            .Select(p => (Ok: DateTime.TryParse(p.Date, out var d), Date: d, Point: p))
            .Where(x => x.Ok && (start == null || x.Date.Date >= start.Value.Date))
            .OrderBy(x => x.Date)
            .ToList();

        if (filtered.Count == 0) return [];

        decimal baseNav = filtered[0].Point.Nav;
        if (baseNav <= 0) return [];

        var chart = filtered.Select(x => new FundNavChartPoint
        {
            Date = x.Date.Date,
            Nav = x.Point.Nav,
            ReturnPercent = (x.Point.Nav / baseNav - 1m) * 100m
        }).ToList();

        periodReturn = chart[^1].ReturnPercent;
        return chart;
    }
}
