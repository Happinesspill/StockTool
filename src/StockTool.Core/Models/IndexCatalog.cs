namespace StockTool.Core.Models;

public sealed class IndexPreset
{
    public string Name { get; init; } = string.Empty;
    public string DisplayCode { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

public static class IndexCatalog
{
    public const int MaxHomeIndices = 4;

    public static IReadOnlyList<IndexPreset> All { get; } =
    [
        new() { Name = "上证指数", DisplayCode = "000001", Code = "SH000001" },
        new() { Name = "创业板指", DisplayCode = "399006", Code = "SZ399006" },
        new() { Name = "科创50", DisplayCode = "000688", Code = "SH000688" },
        new() { Name = "深证成指", DisplayCode = "399001", Code = "SZ399001" },
        // 北交所指数：腾讯/新浪为 bj899050，东财 secid 仍为 0.899050
        new() { Name = "北证50", DisplayCode = "899050", Code = "BJ899050" },
        new() { Name = "沪深300", DisplayCode = "000300", Code = "SH000300" },
        new() { Name = "上证50", DisplayCode = "000016", Code = "SH000016" },
        new() { Name = "恒生指数", DisplayCode = "HSI", Code = "IX100.HSI" },
        // 东财 100.NDX；腾讯 usNDX
        new() { Name = "纳斯达克", DisplayCode = "NDX", Code = "IX100.NDX" },
        new() { Name = "标普500", DisplayCode = "SPX", Code = "IX100.SPX" },
        new() { Name = "道琼斯", DisplayCode = "DJI", Code = "IX100.DJIA" },
    ];

    public static IReadOnlyList<string> DefaultHomeCodes { get; } =
    [
        "SH000001",
        "SZ399006",
        "SH000688",
    ];

    public static IndexPreset? Find(string code)
        => All.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
}
