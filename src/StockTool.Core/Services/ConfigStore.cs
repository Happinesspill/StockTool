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

    // DeepSeek / 小米 MiMo API Key 本地文件，不进 Git
    private static string SecretsPath =>
        Path.Combine(ConfigDir, "secrets.json");

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
                config.EnsureColumnWidthsMigrated();
                ApplyApiSecrets(config, json);
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
        fresh.EnsureColumnWidthsMigrated();
        ApplyApiSecrets(fresh, null);
        return fresh;
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            SaveApiSecrets(config);
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

    private sealed class ApiSecrets
    {
        public string DeepSeekApiKey { get; set; } = string.Empty;
        public string MimoApiKey { get; set; } = string.Empty;
    }

    // 优先读 secrets.json；旧版写在 config.json 中的 Key 迁入本地文件
    private void ApplyApiSecrets(AppConfig config, string? configJson)
    {
        var secrets = ReadApiSecrets();
        if (secrets != null)
        {
            config.DeepSeekApiKey = secrets.DeepSeekApiKey ?? "";
            config.MimoApiKey = secrets.MimoApiKey ?? "";
            return;
        }

        if (string.IsNullOrEmpty(configJson))
            return;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            config.DeepSeekApiKey = ReadJsonString(doc.RootElement, "deepSeekApiKey");
            config.MimoApiKey = ReadJsonString(doc.RootElement, "mimoApiKey");
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(config.DeepSeekApiKey) &&
            string.IsNullOrWhiteSpace(config.MimoApiKey))
            return;

        Save(config);
    }

    private ApiSecrets? ReadApiSecrets()
    {
        try
        {
            if (!File.Exists(SecretsPath))
                return null;
            var json = File.ReadAllText(SecretsPath);
            return JsonSerializer.Deserialize<ApiSecrets>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void SaveApiSecrets(AppConfig config)
    {
        var secrets = new ApiSecrets
        {
            DeepSeekApiKey = config.DeepSeekApiKey ?? "",
            MimoApiKey = config.MimoApiKey ?? ""
        };
        File.WriteAllText(SecretsPath, JsonSerializer.Serialize(secrets, JsonOptions));
    }

    private static string ReadJsonString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? "";
        return "";
    }
}
