using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StockTool.Core.Models;
using StockTool.Data;
using StockTool.Services;

namespace StockTool.Views;

public partial class AddStockDialog : Window
{
    private readonly EastMoneyClient _client;
    private readonly bool _searchFunds;
    private readonly Func<HoldingVisionOptions?>? _getVisionOptions;
    private readonly Action? _openSettings;
    private CancellationTokenSource? _searchCts;
    private List<SearchResultItem> _results = [];
    private readonly ObservableCollection<HoldingImportPreviewItem> _importItems = [];
    private bool _importMode;

    public string? SelectedInternalCode { get; private set; }
    public string? SelectedName { get; private set; }
    public string? SelectedMarket { get; private set; }
    public IReadOnlyList<HoldingImportItem> ImportedHoldings { get; private set; } = [];

    public AddStockDialog(
        EastMoneyClient client,
        bool searchFunds = false,
        Func<HoldingVisionOptions?>? getVisionOptions = null,
        Action? openSettings = null)
    {
        InitializeComponent();
        _client = client;
        _searchFunds = searchFunds;
        _getVisionOptions = getVisionOptions;
        _openSettings = openSettings;
        if (_searchFunds)
        {
            TxtTitle.Text = "添加基金";
            TxtPlaceholder.Text = "输入基金名称或代码搜索\n或 Ctrl+V 粘贴基金持仓详情截图";
            TxtSearch.ToolTip = "输入基金名称或代码搜索，或 Ctrl+V 粘贴基金持仓详情截图";
        }
        else
        {
            TxtPlaceholder.Text = "输入股票名称或代码搜索\n或 Ctrl+V 粘贴持仓截图";
            TxtSearch.ToolTip = "输入股票名称或代码搜索，或 Ctrl+V 粘贴持仓截图";
        }
        TxtSearch.Focus();
    }

    private async void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_importMode) return;

        var keyword = TxtSearch.Text.Trim();
        BtnClear.Visibility = string.IsNullOrEmpty(keyword) ? Visibility.Collapsed : Visibility.Visible;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        if (keyword.Length == 0)
        {
            _results = [];
            LstResults.ItemsSource = null;
            LstResults.Visibility = Visibility.Collapsed;
            TxtPlaceholder.Visibility = Visibility.Visible;
            TxtPlaceholder.Text = _searchFunds
                ? "输入基金名称或代码搜索\n或 Ctrl+V 粘贴基金持仓详情截图"
                : "输入股票名称或代码搜索\n或 Ctrl+V 粘贴持仓截图";
            return;
        }

        try
        {
            await Task.Delay(300, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        var items = _searchFunds
            ? await _client.SearchFundsAsync(keyword)
            : await _client.SearchAsync(keyword);
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
            TxtPlaceholder.Text = _searchFunds ? "未找到匹配的基金" : "未找到匹配的股票";
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

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        ExitImportMode();
    }

    private void ResultItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Child is Grid grid)
        {
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
            AddStock(item);
    }

    private void AddStock(SearchResultItem item)
    {
        SelectedInternalCode = item.InternalCode;
        SelectedName = item.Name;
        SelectedMarket = item.Market;
        DialogResult = true;
        Close();
    }

    private void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        var selected = _importItems
            .Where(i => i.IsSelected && i.CanMatch)
            .Select(i => new HoldingImportItem
            {
                InternalCode = i.InternalCode,
                Name = i.MatchedName,
                Market = i.Market,
                Shares = i.Shares,
                Cost = i.Cost
            })
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请至少勾选一只已匹配的股票", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ImportedHoldings = selected;
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

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsPasteShortcut(e)) return;
        if (ClipboardImageHelper.TryGetImage() is null) return;

        e.Handled = true;
        await ImportFromClipboardAsync();
    }

    private async void TxtSearch_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (ClipboardImageHelper.TryGetImage() is null) return;
        e.CancelCommand();
        await ImportFromClipboardAsync();
    }

    private static bool IsPasteShortcut(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (e.Key == Key.V && ctrl) return true;
        if (e.SystemKey == Key.V && ctrl) return true;
        if (e.Key == Key.ImeProcessed && e.SystemKey == Key.V && ctrl) return true;
        return false;
    }

    private async Task ImportFromClipboardAsync()
    {
        var options = _getVisionOptions?.Invoke();
        if (options is null || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            var result = MessageBox.Show(
                this,
                "请先在设置中填写 DeepSeek 或小米 MiMo 的 API Key，并选择识别服务。\n\n是否打开设置？",
                "未配置识别服务",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
                _openSettings?.Invoke();
            return;
        }

        BitmapSource? image;
        try
        {
            image = ClipboardImageHelper.TryGetImage();
        }
        catch
        {
            MessageBox.Show(this, "无法读取剪贴板图片", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (image is null)
        {
            MessageBox.Show(this, "剪贴板里没有可用图片。微信同步的截图请等同步完成后再粘贴。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string providerLabel = string.Equals(options.Provider, "mimo", StringComparison.OrdinalIgnoreCase)
            ? "小米 MiMo"
            : "DeepSeek";
        string busyText = _searchFunds
            ? $"正在用 {providerLabel} 识别基金持仓截图…"
            : $"正在用 {providerLabel} 识别持仓截图…";
        EnterImportBusy(busyText);
        var (rows, error) = await HoldingVisionService.RecognizeAsync(image, options, isFund: _searchFunds);
        if (!string.IsNullOrEmpty(error))
        {
            ExitImportMode();
            MessageBox.Show(this, error, "识别失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _importItems.Clear();
        foreach (var row in rows)
        {
            var preview = new HoldingImportPreviewItem
            {
                Name = row.Name,
                Code = row.Code,
                Shares = row.Shares,
                Cost = row.Cost,
                PreferHk = row.IsHk,
                Status = "匹配中…"
            };
            _importItems.Add(preview);
        }

        ShowImportList();
        await MatchImportItemsAsync();
    }

    private async Task MatchImportItemsAsync()
    {
        foreach (var item in _importItems.ToList())
        {
            if (_searchFunds)
            {
                await MatchFundItemAsync(item);
                continue;
            }

            var results = await _client.SearchAsync(item.Name);
            var matched = PickBestMatch(results, item.Name, item.PreferHk);
            if (matched is null)
            {
                item.Status = "未匹配到标的";
                item.IsSelected = false;
                continue;
            }

            string? prefix = NormalizeMarket(matched.MarketType);
            if (item.PreferHk && prefix != "HK")
            {
                var hk = results.FirstOrDefault(r =>
                    string.Equals(r.Name, item.Name, StringComparison.OrdinalIgnoreCase)
                    && NormalizeMarket(r.MarketType) == "HK");
                if (hk is not null) matched = hk;
                prefix = NormalizeMarket(matched.MarketType);
            }

            item.InternalCode = prefix != null ? $"{prefix}{matched.Code}" : matched.Code ?? "";
            item.Code = matched.Code ?? "";
            item.MatchedName = matched.Name ?? item.Name;
            item.Market = matched.SecurityTypeName ?? EastMoneyClient.MarketLabelFromPrefix(prefix ?? "SH");
            item.Status = "已匹配";
            item.IsSelected = true;
        }
    }

    private async Task MatchFundItemAsync(HoldingImportPreviewItem item)
    {
        string keyword = !string.IsNullOrWhiteSpace(item.Code) ? item.Code.Trim() : item.Name;
        var results = await _client.SearchFundsAsync(keyword);
        SearchItem? matched = null;

        if (!string.IsNullOrWhiteSpace(item.Code))
        {
            string code = item.Code.Trim();
            matched = results.FirstOrDefault(r =>
                string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
            if (matched is null && code.Length == 6)
            {
                results = await _client.SearchFundsAsync(code);
                matched = results.FirstOrDefault(r =>
                    string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
            }
        }

        matched ??= PickBestMatch(results, item.Name, preferHk: false);
        if (matched is null || string.IsNullOrEmpty(matched.Code))
        {
            item.Status = "未匹配到标的";
            item.IsSelected = false;
            return;
        }

        item.Code = matched.Code!;
        item.InternalCode = EastMoneyClient.ToFundInternalCode(matched.Code!);
        item.MatchedName = matched.Name ?? item.Name;
        item.Market = matched.SecurityTypeName ?? "基金";
        item.Status = "已匹配";
        item.IsSelected = true;
    }

    private static SearchItem? PickBestMatch(List<SearchItem> results, string name, bool preferHk)
    {
        if (results.Count == 0) return null;

        var exact = results
            .Where(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exact.Count > 0)
        {
            if (preferHk)
            {
                var hk = exact.FirstOrDefault(r => NormalizeMarket(r.MarketType) == "HK");
                if (hk is not null) return hk;
            }
            return exact[0];
        }

        var contains = results
            .Where(r => (r.Name ?? "").Contains(name, StringComparison.OrdinalIgnoreCase)
                        || name.Contains(r.Name ?? "", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (contains.Count > 0)
        {
            if (preferHk)
            {
                var hk = contains.FirstOrDefault(r => NormalizeMarket(r.MarketType) == "HK");
                if (hk is not null) return hk;
            }
            return contains[0];
        }

        return results[0];
    }

    private void EnterImportBusy(string message)
    {
        _importMode = true;
        SearchBar.Visibility = Visibility.Collapsed;
        LstResults.Visibility = Visibility.Collapsed;
        LstImport.Visibility = Visibility.Collapsed;
        BtnSync.Visibility = Visibility.Collapsed;
        BtnBack.Visibility = Visibility.Visible;
        TxtPlaceholder.Visibility = Visibility.Visible;
        TxtPlaceholder.Text = message;
        TxtTitle.Text = _searchFunds ? "同步基金持仓" : "同步持仓";
    }

    private void ShowImportList()
    {
        _importMode = true;
        SearchBar.Visibility = Visibility.Collapsed;
        LstResults.Visibility = Visibility.Collapsed;
        LstImport.ItemsSource = _importItems;
        LstImport.Visibility = Visibility.Visible;
        TxtPlaceholder.Visibility = Visibility.Collapsed;
        BtnSync.Visibility = Visibility.Visible;
        BtnBack.Visibility = Visibility.Visible;
        TxtTitle.Text = _searchFunds ? "同步基金持仓" : "同步持仓";
    }

    private void ExitImportMode()
    {
        _importMode = false;
        _importItems.Clear();
        ImportedHoldings = [];
        SearchBar.Visibility = Visibility.Visible;
        LstImport.Visibility = Visibility.Collapsed;
        LstImport.ItemsSource = null;
        BtnSync.Visibility = Visibility.Collapsed;
        BtnBack.Visibility = Visibility.Collapsed;
        TxtTitle.Text = _searchFunds ? "添加基金" : "添加自选";
        BtnClear.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Collapsed : Visibility.Visible;
        TxtPlaceholder.Visibility = Visibility.Visible;
        TxtPlaceholder.Text = _searchFunds
            ? "输入基金名称或代码搜索\n或 Ctrl+V 粘贴基金持仓详情截图"
            : "输入股票名称或代码搜索\n或 Ctrl+V 粘贴持仓截图";
        TxtSearch.Focus();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private static string? NormalizeMarket(string? market)
    {
        if (string.IsNullOrEmpty(market)) return null;

        return market switch
        {
            "SH" or "sh" => "SH",
            "SZ" or "sz" => "SZ",
            "HK" or "hk" => "HK",
            "FD" or "fd" => "FD",
            "1" or "01" => "SH",
            "0" or "00" or "2" or "02" => "SZ",
            "116" => "HK",
            "5" => "HK",
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

public class HoldingImportPreviewItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _status = "";
    private string _code = "";
    private string _market = "";
    private string _internalCode = "";
    private string _matchedName = "";

    public string Name { get; set; } = "";
    public decimal Shares { get; set; }
    public decimal Cost { get; set; }
    public bool PreferHk { get; set; }

    public string SharesText => Shares.ToString("0.####", CultureInfo.InvariantCulture);
    public string CostText => Cost.ToString("0.####", CultureInfo.InvariantCulture);

    public string MatchedName
    {
        get => string.IsNullOrEmpty(_matchedName) ? Name : _matchedName;
        set { _matchedName = value; OnPropertyChanged(); }
    }

    public string Code
    {
        get => _code;
        set { _code = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanMatch)); }
    }

    public string Market
    {
        get => _market;
        set { _market = value; OnPropertyChanged(); }
    }

    public string InternalCode
    {
        get => _internalCode;
        set { _internalCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanMatch)); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(CanMatch));
        }
    }

    public bool CanMatch => !string.IsNullOrEmpty(InternalCode);

    public Brush StatusBrush => CanMatch
        ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
        : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
