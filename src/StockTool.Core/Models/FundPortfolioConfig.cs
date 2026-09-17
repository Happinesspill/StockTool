namespace StockTool.Core.Models;

public class FundPortfolioConfig
{
    public List<WatchlistEntry> Items { get; set; } = [];
    public List<DcaPlan> DcaPlans { get; set; } = [];
    public List<TradeRecord> Trades { get; set; } = [];
}

public class DcaPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FundCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Frequency { get; set; } = "Daily";
    public string ExecuteTime { get; set; } = "15:00";
    public bool Enabled { get; set; } = true;
    public string StartDate { get; set; } = string.Empty;
    /// <summary>已成功入账的最后净值日 yyyy-MM-dd；空表示尚未入账</summary>
    public string LastCatchUpDate { get; set; } = string.Empty;
}

public enum TradeSide
{
    Buy,
    Sell
}

public enum TradeSource
{
    Manual,
    Dca,
    Adjust
}

public class TradeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FundCode { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public TradeSide Side { get; set; }
    public decimal Amount { get; set; }
    public decimal Shares { get; set; }
    public decimal Nav { get; set; }
    public TradeSource Source { get; set; }
}

public class FundNavPoint
{
    public string Date { get; set; } = string.Empty;
    public decimal Nav { get; set; }
    public decimal AccNav { get; set; }
    public decimal ChangePercent { get; set; }
}

public class FundNavChartPoint
{
    public DateTime Date { get; set; }
    public decimal Nav { get; set; }
    public decimal ReturnPercent { get; set; }
}

public class FundTradeMarker
{
    public DateTime Date { get; set; }
    public TradeSide Side { get; set; }
    public decimal Amount { get; set; }
    public decimal Shares { get; set; }
}
