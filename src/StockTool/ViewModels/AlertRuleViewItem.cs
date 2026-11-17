using System.ComponentModel;
using System.Runtime.CompilerServices;
using StockTool.Core.Models;

namespace StockTool.ViewModels;

/// <summary>预警列表展示项：规则条件 + 对应股票的实时行情。</summary>
public sealed class AlertRuleViewItem : INotifyPropertyChanged
{
    private readonly Action<AlertRuleViewItem> _onEnabledChanged;

    public AlertRuleViewItem(AlertRule rule, StockItem? stock, Action<AlertRuleViewItem>? onEnabledChanged = null)
    {
        Rule = rule;
        Stock = stock;
        _onEnabledChanged = onEnabledChanged ?? (_ => { });
    }

    public AlertRule Rule { get; }

    /// <summary>对应股票；已不在自选时为 null。</summary>
    public StockItem? Stock { get; }

    public string StockName => Stock?.DisplayName ?? Rule.StockCode;

    public string CodeText => Stock?.CodeNumeric ?? Rule.StockCode;

    /// <summary>条件说明，如「价格上穿 12.5」。</summary>
    public string ConditionText => Rule.Description;

    /// <summary>当前行情值，取价格或涨跌幅。</summary>
    public string CurrentValueText
    {
        get
        {
            if (Stock == null) return "行情缺失";
            return Rule.Metric == AlertMetric.Price ? Stock.PriceText : Stock.ChangePercentText;
        }
    }

    public bool IsEnabled
    {
        get => Rule.Enabled;
        set
        {
            if (Rule.Enabled == value) return;
            Rule.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsTriggeredToday));
            _onEnabledChanged(this);
        }
    }

    // 今日已命中：规则当天只提醒一次
    public bool IsTriggeredToday =>
        Rule.LastTriggeredDate == DateTime.Now.ToString("yyyy-MM-dd");

    public string StatusText => !Rule.Enabled ? "已停用" : IsTriggeredToday ? "今日已提醒" : "监控中";

    // 行情刷新后更新展示值
    public void Refresh()
    {
        OnPropertyChanged(nameof(StockName));
        OnPropertyChanged(nameof(CodeText));
        OnPropertyChanged(nameof(CurrentValueText));
        OnPropertyChanged(nameof(IsTriggeredToday));
        OnPropertyChanged(nameof(StatusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
