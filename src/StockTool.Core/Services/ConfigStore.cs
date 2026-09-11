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
}
