namespace StockTool.Core.Models;

/// <summary>分时价格序列（时间 + 现价 + 均价）</summary>
public sealed class IntradaySeries
{
    public List<decimal> Prices { get; init; } = [];
    public List<decimal> AvgPrices { get; init; } = [];

    /// <summary>时间点，格式 HH:mm（如 09:30），与 Prices 等长</summary>
    public List<string> Times { get; init; } = [];
}
