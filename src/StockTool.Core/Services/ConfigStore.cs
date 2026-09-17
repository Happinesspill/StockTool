using System.Text.Json;
using StockTool.Core.Models;

namespace StockTool.Core.Services;

public class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StockTool");

    private static string ConfigPath =>
        Path.Combine(ConfigDir, "config.json");

    private static string FundsPath =>
        Path.Combine(ConfigDir, "funds.json");

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
                config.EnsureGroupsMigrated();
                config.EnsureHomeIndicesMigrated();
                config.EnsureHkCodesMigrated();
                return config;
            }
        }
        catch
        {
            // corrupted config, fall through to defaults
        }

        var fresh = new AppConfig();
        fresh.EnsureGroupsMigrated();
        fresh.EnsureHomeIndicesMigrated();
        fresh.EnsureHkCodesMigrated();
        return fresh;
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // silently ignore save failures for now
        }
    }

    public FundPortfolioConfig LoadFunds()
    {
        try
        {
            if (File.Exists(FundsPath))
            {
                var json = File.ReadAllText(FundsPath);
                return JsonSerializer.Deserialize<FundPortfolioConfig>(json, JsonOptions) ?? new FundPortfolioConfig();
            }
        }
        catch
        {
        }

        return new FundPortfolioConfig();
    }

    public void SaveFunds(FundPortfolioConfig funds)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(funds, JsonOptions);
            File.WriteAllText(FundsPath, json);
        }
        catch
        {
        }
    }

    // 将 config.watchlist 中的场外基金迁入 funds.json（一次性）
    public FundPortfolioConfig MigrateFundsFromConfig(AppConfig config)
    {
        var funds = LoadFunds();
        var fundEntries = config.Watchlist.Where(IsFundEntry).ToList();
        if (fundEntries.Count == 0)
            return funds;

        foreach (var entry in fundEntries)
        {
            if (funds.Items.Any(i =>
                    string.Equals(i.Code, entry.Code, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(i.GroupId, entry.GroupId, StringComparison.OrdinalIgnoreCase)))
                continue;

            funds.Items.Add(entry);

            if (entry.HoldingShares > 0 && entry.HoldingCost > 0
                && !funds.Trades.Any(t =>
                    string.Equals(t.FundCode, entry.Code, StringComparison.OrdinalIgnoreCase)
                    && t.Source == TradeSource.Manual))
            {
                funds.Trades.Add(new TradeRecord
                {
                    FundCode = entry.Code,
                    Date = DateTime.Today.ToString("yyyy-MM-dd"),
                    Side = TradeSide.Buy,
                    Amount = entry.HoldingShares * entry.HoldingCost,
                    Shares = entry.HoldingShares,
                    Nav = entry.HoldingCost,
                    Source = TradeSource.Manual
                });
            }
        }

        config.Watchlist.RemoveAll(IsFundEntry);
        Save(config);
        SaveFunds(funds);
        return funds;
    }

    public static bool IsFundEntry(WatchlistEntry entry) =>
        !StockItem.IsExchangeFundCode(entry.Code)
        && (string.Equals(entry.Market, "基金", StringComparison.OrdinalIgnoreCase)
            || entry.Code.StartsWith("FD", StringComparison.OrdinalIgnoreCase));
}
