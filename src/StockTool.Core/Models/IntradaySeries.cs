namespace StockTool.Core.Models;

/// <summary>分时价格序列（现价 + 均价）</summary>
public sealed class IntradaySeries
{
    public List<decimal> Prices { get; init; } = [];
    public List<decimal> AvgPrices { get; init; } = [];
}
