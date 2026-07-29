namespace StockTool.Core.Models;

public class AppConfig
{
    public List<WatchlistEntry> Watchlist { get; set; } = [];
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 420;
    public double WindowHeight { get; set; } = 600;
    public double Opacity { get; set; } = 0.9;
    public int FontSize { get; set; } = 14;
    public int RefreshInterval { get; set; } = 1;
    public string Hotkey { get; set; } = "Ctrl+Shift+S";
    public bool Topmost { get; set; } = true;
    public bool ShowMarketTag { get; set; } = true;
}

public class WatchlistEntry
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string CustomName { get; set; } = string.Empty;
}
