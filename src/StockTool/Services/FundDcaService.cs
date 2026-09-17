using System.Windows.Threading;
using StockTool.Core.Models;
using StockTool.Core.Services;

namespace StockTool.Services;

// 基金定投：按已公布净值入账，启动与盘后补算
public class FundDcaService
{
    private readonly ConfigStore _configStore;
    private readonly FundNavService _navService;
    private readonly DispatcherTimer _timer;
    private FundPortfolioConfig _funds;
    private Action? _onHoldingsChanged;

    public FundDcaService(ConfigStore configStore, FundNavService navService, FundPortfolioConfig funds)
    {
        _configStore = configStore;
        _navService = navService;
        _funds = funds;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _timer.Tick += async (_, _) => await CatchUpAsync();
    }

    public void SetFunds(FundPortfolioConfig funds) => _funds = funds;

    public void SetHoldingsChangedHandler(Action handler) => _onHoldingsChanged = handler;

    public void Start()
    {
        _timer.Start();
        _ = CatchUpAsync();
    }

    public void Stop() => _timer.Stop();

    public async Task CatchUpAsync()
    {
        bool changed = false;
        var now = DateTime.Now;
        bool afterExecute = now.TimeOfDay >= new TimeSpan(15, 0, 0);

        foreach (var plan in _funds.DcaPlans.Where(p => p.Enabled && p.Amount > 0).ToList())
        {
            if (!DateTime.TryParse(plan.StartDate, out var startDate))
                startDate = DateTime.Today;

            DateTime cursor;
            if (DateTime.TryParse(plan.LastCatchUpDate, out var last))
                cursor = last.Date.AddDays(1);
            else
                cursor = startDate.Date;

            DateTime end = afterExecute ? now.Date : now.Date.AddDays(-1);
            if (end < cursor) continue;

            for (var day = cursor; day <= end; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                string ymd = day.ToString("yyyy-MM-dd");
                bool already = _funds.Trades.Any(t =>
                    string.Equals(t.FundCode, plan.FundCode, StringComparison.OrdinalIgnoreCase)
                    && t.Source == TradeSource.Dca
                    && string.Equals(t.Date, ymd, StringComparison.OrdinalIgnoreCase));
                if (already)
                {
                    plan.LastCatchUpDate = ymd;
                    changed = true;
                    continue;
                }

                var nav = await _navService.EnsureNavAsync(plan.FundCode, ymd);
                if (nav == null || nav.Nav <= 0)
                    break;

                decimal shares = plan.Amount / nav.Nav;
                _funds.Trades.Add(new TradeRecord
                {
                    FundCode = plan.FundCode,
                    Date = ymd,
                    Side = TradeSide.Buy,
                    Amount = plan.Amount,
                    Shares = shares,
                    Nav = nav.Nav,
                    Source = TradeSource.Dca
                });

                ApplyBuyToHolding(plan.FundCode, plan.Amount, shares);
                plan.LastCatchUpDate = ymd;
                changed = true;
            }
        }

        if (changed)
        {
            _configStore.SaveFunds(_funds);
            _onHoldingsChanged?.Invoke();
        }
    }

    private void ApplyBuyToHolding(string fundCode, decimal amount, decimal shares)
    {
        var item = _funds.Items.FirstOrDefault(i =>
            string.Equals(i.Code, fundCode, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;

        decimal oldShares = item.HoldingShares;
        decimal oldCost = item.HoldingCost;
        decimal newShares = oldShares + shares;
        if (newShares <= 0)
        {
            item.HoldingShares = 0;
            item.HoldingCost = 0;
            return;
        }

        decimal oldBasis = oldShares > 0 && oldCost > 0 ? oldShares * oldCost : 0;
        item.HoldingShares = newShares;
        item.HoldingCost = (oldBasis + amount) / newShares;
    }
}
