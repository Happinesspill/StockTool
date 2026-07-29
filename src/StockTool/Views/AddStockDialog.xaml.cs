using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StockTool.Core.Models;
using StockTool.Data;

namespace StockTool.Views;

public partial class AddStockDialog : Window
{
    private readonly EastMoneyClient _client;
    private CancellationTokenSource? _searchCts;
    private List<SearchResultItem> _results = [];

    public string? SelectedInternalCode { get; private set; }
    public string? SelectedName { get; private set; }
    public string? SelectedMarket { get; private set; }

    public AddStockDialog(EastMoneyClient client)
    {
        InitializeComponent();
        _client = client;
        TxtSearch.Focus();
    }

    private async void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var keyword = TxtSearch.Text.Trim();
        BtnClear.Visibility = string.IsNullOrEmpty(keyword) ? Visibility.Collapsed : Visibility.Visible;

        // Cancel previous search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        if (keyword.Length == 0)
        {
            _results = [];
            LstResults.ItemsSource = null;
            LstResults.Visibility = Visibility.Collapsed;
            TxtPlaceholder.Visibility = Visibility.Visible;
            TxtPlaceholder.Text = "输入股票名称或代码搜索";
            return;
        }

        // Debounce 300ms
        try
        {
            await Task.Delay(300, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        var items = await _client.SearchAsync(keyword);
        if (token.IsCancellationRequested) return;

        _results = items
            .Where(i => !string.IsNullOrEmpty(i.Code) && !string.IsNullOrEmpty(i.Name))
            .Select(i =>
            {
                string? prefix = NormalizeMarket(i.MarketType);
                string internalCode = prefix != null ? $"{prefix}{i.Code}" : i.Code!;
                string marketLabel = i.SecurityTypeName ?? EastMoneyClient.MarketLabelFromPrefix(prefix ?? "SH");
                return new SearchResultItem
                {
                    Name = i.Name!,
                    Code = i.Code!,
                    Market = marketLabel,
                    InternalCode = internalCode
                };
            })
            .ToList();

        if (token.IsCancellationRequested) return;

        if (_results.Count == 0)
        {
            LstResults.ItemsSource = null;
            LstResults.Visibility = Visibility.Collapsed;
            TxtPlaceholder.Visibility = Visibility.Visible;
            TxtPlaceholder.Text = "未找到匹配的股票";
        }
        else
        {
            LstResults.ItemsSource = _results;
            LstResults.Visibility = Visibility.Visible;
            TxtPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = "";
        TxtSearch.Focus();
    }

    private void ResultItem_Click(object sender, MouseButtonEventArgs e)
    {
        // Click on the row itself to add
        if (sender is Border border && border.Child is Grid grid)
        {
            // Find the Button inside and get its tag
            foreach (var child in grid.Children)
            {
                if (child is Button btn && btn.Tag is SearchResultItem item)
                {
                    AddStock(item);
                    return;
                }
            }
        }
    }

    private void BtnAddResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SearchResultItem item)
        {
            AddStock(item);
        }
    }

    private void AddStock(SearchResultItem item)
    {
        SelectedInternalCode = item.InternalCode;
        SelectedName = item.Name;
        SelectedMarket = item.Market;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    /// <summary>
    /// Normalize EastMoney market field to "SH"/"SZ"/"HK" prefix.
    /// The API may return numeric codes or string prefixes.
    /// </summary>
    private static string? NormalizeMarket(string? market)
    {
        if (string.IsNullOrEmpty(market)) return null;

        return market switch
        {
            "SH" or "sh" => "SH",
            "SZ" or "sz" => "SZ",
            "HK" or "hk" => "HK",
            // Numeric market codes
            "1" or "01" => "SH",
            "0" or "00" or "2" or "02" => "SZ",
            "116" => "HK",
            _ => market.Length <= 3 ? market.ToUpper() : null
        };
    }
}

public class SearchResultItem
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Market { get; set; } = "";
    public string InternalCode { get; set; } = "";
}
