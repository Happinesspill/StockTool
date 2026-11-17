namespace StockTool.Services;

/// <summary>
/// 持仓截图简称与东财现用名称不一致时的固定映射。
/// 上海金已更名为金ETF广发（518600），按「上海金」搜索会配到别的标的。
/// </summary>
public static class HoldingSyncAliases
{
    private readonly record struct Target(string InternalCode, string Name);

    private static readonly Dictionary<string, Target> ByAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["上海金"] = new("SH518600", "金ETF广发"),
        ["上海金ETF"] = new("SH518600", "金ETF广发"),
        ["广发上海金"] = new("SH518600", "金ETF广发"),
        ["广发上海金ETF"] = new("SH518600", "金ETF广发"),
    };

    public static bool TryResolve(string? name, out string internalCode, out string canonicalName)
    {
        internalCode = "";
        canonicalName = "";
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!ByAlias.TryGetValue(name.Trim(), out var hit)) return false;
        internalCode = hit.InternalCode;
        canonicalName = hit.Name;
        return true;
    }

    /// <summary>自定义名仍是该代码的旧简称时，应让持仓显示东财现名。</summary>
    public static bool IsStaleDisplayName(string? code, string? customName)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(customName))
            return false;

        string alias = customName.Trim();
        if (!ByAlias.TryGetValue(alias, out var hit))
            return false;

        return string.Equals(hit.InternalCode, code.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
