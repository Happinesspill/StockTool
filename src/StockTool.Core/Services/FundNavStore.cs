using System.Text.Json;
using StockTool.Core.Models;

namespace StockTool.Core.Services;

// 基金净值日线本地缓存
public class FundNavStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StockTool", "fund-nav");

    private static string PathFor(string fundCode)
    {
        string code = fundCode.StartsWith("FD", StringComparison.OrdinalIgnoreCase) ? fundCode[2..] : fundCode;
        return Path.Combine(CacheDir, $"{code}.json");
    }

    public List<FundNavPoint> Load(string fundCode)
    {
        try
        {
            string path = PathFor(fundCode);
            if (!File.Exists(path)) return [];
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<FundNavPoint>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(string fundCode, List<FundNavPoint> points)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var json = JsonSerializer.Serialize(points, JsonOptions);
            File.WriteAllText(PathFor(fundCode), json);
        }
        catch
        {
        }
    }

    public FundNavPoint? FindByDate(string fundCode, string dateYmd)
    {
        return Load(fundCode).FirstOrDefault(p =>
            string.Equals(p.Date, dateYmd, StringComparison.OrdinalIgnoreCase));
    }
}
