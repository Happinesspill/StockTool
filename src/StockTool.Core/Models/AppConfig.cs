using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StockTool.Core.Models;

public class AppConfig
{
    public List<WatchlistEntry> Watchlist { get; set; } = [];
    public List<WatchlistGroup> Groups { get; set; } = [];
    public List<AlertRule> AlertRules { get; set; } = [];
    /// <summary>首页指数内部 Code，最多 4 个。</summary>
    public List<string> HomeIndexCodes { get; set; } = [];
    public string SelectedGroupId { get; set; } = string.Empty;
    /// <summary>旧字段：default | change。保留用于迁移。</summary>
    public string SortMode { get; set; } = "default";
    public string SortColumn { get; set; } = "default";
    public bool SortDescending { get; set; } = true;
    public bool IsEditMode { get; set; }
    public string ListDensity { get; set; } = "moderate";
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
    /// <summary>持仓截图识别：deepseek | mimo</summary>
    public string HoldingVisionProvider { get; set; } = "deepseek";
    public string DeepSeekApiKey { get; set; } = string.Empty;
    public string MimoApiKey { get; set; } = string.Empty;

    /// <summary>旧配置无分组时迁移为「自选」+ 空「港股」「ETF」「基金」；「自选」固定首位。</summary>
    public void EnsureGroupsMigrated()
    {
        if (Groups.Count == 0)
        {
            var zixuan = new WatchlistGroup { Id = Guid.NewGuid().ToString("N"), Name = "自选" };
            var ganggu = new WatchlistGroup { Id = Guid.NewGuid().ToString("N"), Name = "港股" };
            var etf = new WatchlistGroup { Id = Guid.NewGuid().ToString("N"), Name = "ETF" };
            var jijin = new WatchlistGroup { Id = Guid.NewGuid().ToString("N"), Name = "基金" };
            Groups = [zixuan, ganggu, etf, jijin];
            SelectedGroupId = zixuan.Id;
            foreach (var entry in Watchlist)
                entry.GroupId = zixuan.Id;
            EnsureHomeIndicesMigrated();
            return;
        }

        // 移除历史上误存的「持仓」实分组，股票归入「自选」
        var legacyHolding = Groups.Where(g => g.Name == WatchlistGroup.HoldingGroupName).ToList();
        var zixuanGroup = Groups.FirstOrDefault(g => g.Name == "自选");
        if (zixuanGroup == null)
        {
            zixuanGroup = new WatchlistGroup { Id = Guid.NewGuid().ToString("N"), Name = "自选" };
            Groups.Insert(0, zixuanGroup);
        }

        foreach (var hg in legacyHolding)
        {
            foreach (var entry in Watchlist.Where(e => e.GroupId == hg.Id))
                entry.GroupId = zixuanGroup.Id;
            Groups.Remove(hg);
        }

        int zixuanIndex = Groups.IndexOf(zixuanGroup);
        if (zixuanIndex > 0)
        {
            Groups.RemoveAt(zixuanIndex);
            Groups.Insert(0, zixuanGroup);
        }

        string fallback = Groups[0].Id;
        foreach (var entry in Watchlist)
        {
            if (string.IsNullOrEmpty(entry.GroupId) || Groups.All(g => g.Id != entry.GroupId))
                entry.GroupId = fallback;
        }

        if (Groups.All(g => g.Name != "基金"))
            Groups.Add(new WatchlistGroup { Id = Guid.NewGuid().ToString("N"), Name = "基金" });

        if (string.IsNullOrEmpty(SelectedGroupId) ||
            (SelectedGroupId != WatchlistGroup.HoldingGroupId && Groups.All(g => g.Id != SelectedGroupId)))
            SelectedGroupId = Groups[0].Id;

        if (SortMode is not ("default" or "change"))
            SortMode = "default";
        if (SortColumn == "default" && SortMode == "change")
            SortColumn = "change";
        if (SortColumn is not ("default" or "price" or "change" or "profit" or "holdingAmount" or "turnover" or "turnoverRate" or "speed" or "volumeRatio" or "marketValue"))
            SortColumn = "default";
        if (ListDensity is not ("moderate" or "compact"))
            ListDensity = "moderate";

        EnsureHomeIndicesMigrated();
    }

    public void EnsureHomeIndicesMigrated()
    {
        if (HomeIndexCodes.Count == 0)
        {
            HomeIndexCodes = IndexCatalog.DefaultHomeCodes.ToList();
            return;
        }

        HomeIndexCodes = HomeIndexCodes
            .Where(c => IndexCatalog.Find(c) != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(IndexCatalog.MaxHomeIndices)
            .ToList();

        if (HomeIndexCodes.Count == 0)
            HomeIndexCodes = IndexCatalog.DefaultHomeCodes.ToList();
    }

    /// <summary>修正历史港股代码：东财市场码 "5" 曾被当成前缀，使 03690 存成 503690。</summary>
    public void EnsureHkCodesMigrated()
    {
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Watchlist)
        {
            if (!string.Equals(entry.Market, "港股", StringComparison.OrdinalIgnoreCase)) continue;

            string code = ToHkInternalCode(entry.Code);
            if (string.Equals(code, entry.Code, StringComparison.OrdinalIgnoreCase)) continue;

            renamed[entry.Code] = code;
            entry.Code = code;
        }

        // 预警规则没有市场字段，只按上面确认过的映射改名，避免误改 A 股/场内基金代码
        foreach (var rule in AlertRules)
        {
            if (renamed.TryGetValue(rule.StockCode, out var code))
                rule.StockCode = code;
        }
    }

    /// <summary>港股内部代码规范化：503690 → HK03690（已是 HKxxxxx 时原样返回）</summary>
    private static string ToHkInternalCode(string code)
    {
        string digits = new(code.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return code;

        // 港股标准为 5 位代码，6 位数字说明带着历史错误前缀
        if (digits.Length == 6 && digits[0] == '5') digits = digits[1..];
        return "HK" + digits.PadLeft(5, '0');
    }
}

public enum AlertMetric { Price, ChangePercent }
public enum AlertDirection { Above, Below }

public class AlertRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StockCode { get; set; } = string.Empty;
    public AlertMetric Metric { get; set; }
    public AlertDirection Direction { get; set; }
    public decimal Threshold { get; set; }
    public bool Enabled { get; set; } = true;
    public string LastTriggeredDate { get; set; } = string.Empty;
    public string Description => Metric == AlertMetric.Price
        ? $"价格{(Direction == AlertDirection.Above ? "上穿" : "下破")} {Threshold:0.##}"
        : $"涨跌幅{(Direction == AlertDirection.Above ? "上穿" : "下破")} {Threshold:+0.##;-0.##;0.##}%";
}

public class WatchlistGroup : INotifyPropertyChanged
{
    public const string HoldingGroupId = "__holding__";
    public const string HoldingGroupName = "持仓";

    private string _id = string.Empty;
    private string _name = string.Empty;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class WatchlistEntry
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string CustomName { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public decimal HoldingShares { get; set; }
    public decimal HoldingCost { get; set; }
}

// 持仓截图导入项
public class HoldingImportItem
{
    public string InternalCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public decimal Shares { get; set; }
    public decimal Cost { get; set; }
}
