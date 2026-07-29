# 盯盘 (StockTool) — Project Summary

## 1. Project Overview

**盯盘** is a real-time stock monitoring desktop tool built with **WPF on .NET 9**. It provides a lightweight, always-on-top floating window that shows live quotes for a user-curated watchlist covering A-shares (Shanghai/Shenzhen), Hong Kong stocks, and A-share ETFs. Data is sourced from free public HTTP APIs provided by **东方财富 (East Money)**, requiring no API key.

The tool is designed for day traders and casual investors who want a glanceable, unobtrusive market monitor that stays out of the way via system tray integration and a global hotkey to toggle visibility.

| Property | Detail |
|----------|--------|
| Tech stack | WPF + .NET 9 + Windows Forms (for NotifyIcon) |
| Language | C# 13 |
| Target OS | Windows 10/11 |
| UI framework | WPF with `AllowsTransparency`, no window chrome |
| Data source | East Money public HTTP APIs |
| Output type | `WinExe` (single self-contained desktop app) |

---

## 2. Architecture

The solution is partitioned into three projects, cleanly separated by concern:

```
StockTool.slnx
└── /src/
    ├── StockTool.Core/      # Models, config store (no UI or network dependencies)
    ├── StockTool.Data/      # East Money HTTP client & API parsing
    └── StockTool/           # WPF UI, ViewModels, services, controls, converters
```

### Dependency Graph

```
StockTool (WPF app, net9.0-windows)
  ├── references StockTool.Core (net9.0)
  └── references StockTool.Data (net9.0)

StockTool.Data (net9.0)
  └── references StockTool.Core (net9.0)

StockTool.Core (net9.0)
  (no project dependencies — pure model + config)
```

### StockTool.Core — Models & Configuration

- **`AppConfig.cs`** — Configuration POCO: watchlist entries, window position/size, opacity, font size, refresh interval, hotkey string, topmost flag, show-market-tag flag. Defaults are baked in (opacity 0.9, font size 14, 1s refresh, `Ctrl+Shift+S` hotkey, topmost on).
- **`WatchlistEntry.cs`** — A single watchlist row: `Name`, `Code` (internal format like `SH600036`), `Market` label (`沪A`/`深A`/`港股`/`ETF`), and an optional `CustomName` for user-defined aliases.
- **`QuoteData.cs`** — DTOs for deserializing East Money JSON responses:
  - `QuoteItem` fields: F2=price, F3=change%, F5=volume(shares), F6=turnover(amount), F12=code, F13=market, F14=name, F15=high, F16=low, F17=yesterday close, F18=open.
  - `IntradayResponse` / `IntradayDataWrapper` for intraday trend CSV lines.
  - `SearchResponse` / `SearchItem` for stock search suggestions.
  - `KlineResponse` / `KlineDataWrapper` / `KlineItem` for K-line (candlestick) data. `KlineItem.FromCsv()` parses `日期,开盘,收盘,最高,最低,成交量,...` format.
- **`StockItem.cs`** — Observable ViewModel entity per stock row. Properties:
  - Basic: `Name`, `CustomName`, `DisplayName` (computed), `Code` (`SH600036`), `CodeNumeric` (`600036`), `Market` (`沪A`).
  - Quote: `CurrentPrice`, `ChangePercent`, `YesterdayClose`, `Open`, `High`, `Low`, `Volume`, `Turnover`.
  - Computed display: `IsUp` (`ChangePercent >= 0` — drives sparkline color), `VolumeText` (auto-scale to 手/万手/亿手), `TurnoverText` (auto-scale to 万/亿).
  - Chart data: `IntradayPoints` (List<decimal> for sparkline), `KlineData` (List<KlineItem> for candlestick chart), `KlinePeriod` (int: 5/30/101/102).
  - Implements `INotifyPropertyChanged`.
- **`ConfigStore.cs`** — JSON file persistence service. Reads/writes `AppConfig` to `%AppData%/StockTool/config.json` using `System.Text.Json` with `camelCase` naming and indentation. Gracefully handles missing or corrupted files by returning defaults.

### StockTool.Data — API Client

- **`EastMoneyClient.cs`** — The sole class responsible for all external HTTP communication.
  - Uses a single reusable `HttpClient` with an 8-second timeout and a browser-like User-Agent header.
  - **`GetBatchQuotesAsync(codes)`** — Calls `push2.eastmoney.com/api/qt/ulist.np/get` with comma-separated `secid`s. Fields requested: `f2,f3,f4,f5,f6,f12,f13,f14,f15,f16,f17,f18` covering current price, change%, change amount, volume, turnover, code, market, name, high, low, yesterday close, open.
  - **`GetIntradayAsync(code)`** — Calls `push2his.eastmoney.com/api/qt/stock/trends2/get` for 1-day intraday minute-level price data. Parses CSV lines; extracts the second field (price).
  - **`SearchAsync(keyword)`** — Calls `searchapi.eastmoney.com/api/suggest/get` with `type=14`. Returns up to 10 results with code, name, market type, security type name.
  - **`GetKlineAsync(code, period, count)`** — Calls `push2his.eastmoney.com/api/qt/stock/kline/get`. `period`: 5=5min, 30=30min, 101=daily, 102=weekly, 103=monthly. `count`: max bars (default 80). Returns `List<KlineItem>` parsed from CSV.
  - Static helpers: `ToSecId()` maps internal `SH600036` → East Money's `1.600036`; `InternalCodeFromSecId()` reverses it; `MarketLabelFromPrefix()` maps `SH`→`沪A`, `SZ`→`深A`, `HK`→`港股`.

### StockTool — WPF Application

The main application project contains all UI, ViewModels, WPF services, controls, and value converters.

---

## 3. Features List

### Implemented (MVP — all phases complete)

| # | Feature | Detail |
|---|---------|--------|
| 1 | **Watchlist management** | Add stocks via search dialog; remove, rename, reorder (move up/down) via right-click context menu |
| 2 | **Real-time quotes** | Batch polling of all watchlist stocks at configurable 1–5 second intervals via East Money API |
| 3 | **Intraday sparkline** | Minute-level price chart rendered per row via custom `SparklineControl` (WPF `OnRender`/`StreamGeometry`) |
| 4 | **Multi-market support** | A-shares (Shanghai/Shenzhen), Hong Kong stocks, A-share ETFs — unified internal code format (`SH`/`SZ`/`HK` prefix) |
| 5 | **Red-up / green-down coloring** | Chinese convention: price up = red (`#D93025`), price down = green (`#1A8C3F`), unchanged = gray |
| 6 | **Borderless always-on-top window** | `WindowStyle=None`, `AllowsTransparency=True`, `Topmost` bindable; draggable via title bar `DragMove()` |
| 7 | **Adjustable transparency** | 0–100% slider in settings; background opacity controlled via `OpacityToBrushConverter` applied to the outer `Border` |
| 8 | **Adjustable font size** | 12–20 range via settings combo box; applied through `FontSizeConverter` with per-element offsets |
| 9 | **Configurable refresh interval** | 1–5 seconds for quotes; downtimes 5× slower (min 10s); intraday refreshes every 60s (trading) / 300s (off-hours) |
| 10 | **Global hotkey** | Default `Ctrl+Shift+S` toggles window visibility; powered by Win32 `RegisterHotKey` P/Invoke; "record" mode in settings to change key |
| 11 | **System tray integration** | `NotifyIcon` with context menu (Show / Settings / Exit); double-click to restore; closing window hides to tray instead of exiting |
| 12 | **Persistent configuration** | All settings (watchlist, window position/size, opacity, font, hotkey, etc.) saved to `%AppData%/StockTool/config.json` |
| 13 | **Market tag display** | Optional colored badge per row showing market label (沪A/深A/港股/ETF); toggleable in settings |
| 14 | **Trading hours awareness** | `TradingHours` static utility detects A-share (9:30–11:30, 13:00–15:00) and HK (9:30–12:00, 13:00–16:00) sessions; adjusts refresh rates dynamically; excludes weekends |
| 15 | **Custom stock names** | Right-click → Rename to assign a user-friendly alias; displayed instead of the API-provided name |
| 16 | **Resizable window** | `ResizeMode=CanResizeWithGrip` with min dimensions 320×300; position and size persisted |
| 17 | **Empty state** | "暂无自选股，点击 + 添加" placeholder text when watchlist is empty |
| 18 | **Stock search dialog** | Debounced (300ms) search-as-you-type against East Money suggest API; results show name, code, market; click row or '+' button to add |
| 19 | **Default watchlist** | 7 pre-configured stocks on first launch: 招商银行, 中国平安, 小米集团, 新华保险, 海尔智家, 沪深300ETF, 黄金股ETF |
| 20 | **Sparkline color fix** | Sparkline color driven by `ChangePercent >= 0` (IsUp property) instead of comparing last intraday point vs baseline; matches the price display color |
| 21 | **Expandable detail panel** | Click any stock row → bottom panel slides up showing: stock name/code/price, 今开/最高/最低/成交额, period selector (5分/30分/日线/周线), interactive K-line candlestick chart with volume bars |
| 22 | **K-line chart** | Custom `KlineChartControl` renders candle bodies (red up / green down), wicks, and volume bars; supports 4 time periods loaded on demand from East Money API |
| 23 | **Extended quote fields** | Open price, high, low, volume, turnover now fetched with each batch quote update; volume/turnover auto-formatted (手/万手/亿手, 万/亿) |

---

## 4. UI Components

### Main Window

A floating, rounded-corner (8px radius), borderless window with three vertical zones:

```
┌─────────────────────────────────────────────────┐
│  盯盘                    🔍  ⚙  −  ✕          │  ← Title bar (36px)
├─────────────────────────────────────────────────┤
│  招商银行          ┌──────────┐   39.05         │
│  [沪A] 600036      │ sparkline│   +0.36%        │  ← Stock row (52px)
│                    └──────────┘                 │
│  小米集团          ┌──────────┐   24.80         │
│  [港股] 01810      │ sparkline│   -0.74%        │
│                    └──────────┘                 │
│  ...                                           │
├─────────────────────────────────────────────────┤
│  招商银行  600036                    39.05      │
│  今开 39.18   今开  最高  最低  成交额           │  ← Detail panel (Auto)
│  [5分] [30分] [日线] [周线]                      │     visible when a stock
│  ┌── K线图(蜡烛+成交量) ───────────────────┐    │     is clicked
│  └─────────────────────────────────────────┘    │
├─────────────────────────────────────────────────┤
│  ● 数据来源：东方财富 | 仅供参考                  │  ← Status bar (24px)
└─────────────────────────────────────────────────┘
```

**Key visual elements:**
- **Title bar** — Semi-transparent white background. Buttons: search/add (`🔍`), settings (`⚙`), hide to tray (`−`), exit (`✕`). Close button turns red on hover.
- **Stock rows** — Three-column layout: left (120px) with bold name + market badge + code, center with sparkline, right (100px) with price + change%.
- **Context menu** — Stock header, then: ↑ Move Up, ↓ Move Down, ✎ Rename, ✕ Delete (red).
- **Detail panel** — Fixed at bottom; contents bound to `SelectedStock`. Shows: name/code header, current price + change%, 今开/最高/最低/成交额 in 4-column grid, period selector tabs, K-line candlestick chart.
- **Color scheme** — Light theme: white backgrounds, black/dark text (`#1A1A1A` body, `#888888` secondary), red up (`#D93025`), green down (`#1A8C3F`).

### Add Stock Dialog

- 400×440, center-on-owner, rounded border (12px corner radius).
- Rounded pill-shaped search bar with 🔍 icon and ✕ clear button.
- Search is debounced at 300ms with `CancellationTokenSource` for cancellation.
- Results show name (bold) + code + market label; circular `+` button on each row; entire row is clickable.
- Rounded blue selected state (`#E6F0FA` background, `#B0D0F0` border).

### Settings Window

- 340px wide, auto-sized height, center-on-owner.
- Controls: opacity slider (0–100%), font size combo (12–20), refresh interval combo (1–5), hotkey text + record button, topmost checkbox, market tag checkbox.
- All inputs use rounded styles (`CornerRadius="6"`), text centered.
- Buttons: blue save (`#2563EB`), gray cancel, both with rounded corners.

### Rename Dialog

- Programmatically created overlay, 320×160, center-on-owner, topmost.
- Rounded input box (`CornerRadius="6"`) with centered text.
- Blue OK button (`#2563EB`), gray cancel button — both rounded.

---

## 5. File Structure

```
D:/Code/Demo/StockTool/
├── StockTool.slnx                          # Solution file
├── 盯盘桌面小工具实现规划.md                  # Original planning doc
├── docs/
│   ├── IMPLEMENTATION.md                   # Implementation plan
│   └── PROJECT_SUMMARY.md                  # This document
├── publish/                                # Release build output
└── src/
    ├── StockTool.Core/
    │   ├── Models/
    │   │   ├── AppConfig.cs                # Config POCO + WatchlistEntry
    │   │   ├── QuoteData.cs                # API response DTOs
    │   │   └── StockItem.cs                # Observable stock row entity
    │   └── Services/
    │       └── ConfigStore.cs              # JSON file persistence
    │
    ├── StockTool.Data/
    │   └── EastMoneyClient.cs              # HTTP client for 3 East Money endpoints
    │
    └── StockTool/
        ├── App.xaml / App.xaml.cs           # App resources, tray icon, lifecycle
        ├── MainWindow.xaml / .xaml.cs       # Main window layout & code-behind
        ├── Controls/
        │   ├── SparklineControl.cs          # Custom intraday sparkline renderer
        │   └── KlineChartControl.cs         # Custom K-line candlestick + volume chart
        ├── Converters/
        │   ├── FontSizeConverter.cs          # Base font + offset
        │   ├── NegativeToBooleanConverter.cs # decimal < 0 → true
        │   ├── NotNullToVisibleConverter.cs  # object != null → Visible
        │   ├── OpacityToBrushConverter.cs    # opacity → alpha-scaled brush
        │   └── ZeroToVisibleConverter.cs     # count==0 → Visible
        ├── Services/
        │   ├── HotkeyService.cs             # Win32 RegisterHotKey P/Invoke
        │   ├── IntradayService.cs           # Intraday data fetch timer
        │   ├── QuoteService.cs              # Batch quote polling timer
        │   └── TradingHours.cs              # Trading calendar logic
        ├── Themes/
        │   └── Generic.xaml                 # SparklineControl default style
        ├── ViewModels/
        │   └── MainViewModel.cs             # Main DataContext
        └── Views/
            ├── AddStockDialog.xaml / .cs     # Search & add stock dialog
            └── SettingsWindow.xaml / .cs     # Settings dialog
```

---

## 6. Key Technical Details

### APIs (East Money — no authentication required)

| API | Endpoint | Purpose | Caller |
|-----|----------|---------|--------|
| Batch quotes | `push2.eastmoney.com/api/qt/ulist.np/get` | Real-time price, change%, name, open/high/low, volume/turnover, yesterday close | `QuoteService` (1–5s) |
| Intraday trends | `push2his.eastmoney.com/api/qt/stock/trends2/get` | 1-day minute-level price points | `IntradayService` (60–300s) |
| Search suggest | `searchapi.eastmoney.com/api/suggest/get` | Stock name/code autocomplete | `AddStockDialog` (300ms debounce) |
| K-line data | `push2his.eastmoney.com/api/qt/stock/kline/get` | OHLCV candlestick bars for given period | `MainWindow` code-behind (on stock click / period switch) |

**Internal code format**: `{MarketPrefix}{NumericCode}` (e.g., `SH600036`). East Money secid: `{marketId}.{code}` (market IDs: 1=SH, 0=SZ, 116=HK).

**K-line period codes**: 5=5min, 30=30min, 101=daily, 102=weekly, 103=monthly. Requested with `fqt=1` (forward-adjusted prices).

### Value Converters

| Converter | Purpose |
|-----------|---------|
| `FontSizeConverter` | Base font + offset for size variants |
| `NegativeToBooleanConverter` | decimal < 0 → true (drives green color trigger) |
| `NotNullToVisibleConverter` | object != null → Visible (drives detail panel visibility) |
| `OpacityToBrushConverter` | 0.0–1.0 → alpha-scaled brush; optional hex color parameter |
| `ZeroToVisibleConverter` | count==0 → Visible (empty state placeholder) |

### Services

- **`QuoteService`** — DispatcherTimer polls batch quotes every 1s; on each tick, calls `UpdateInterval()` which adjusts based on trading hours. Maps all quote fields (price, change%, open, high, low, volume, turnover, yesterday close) to `StockItem` via UI dispatcher. Silent failure on network errors.
- **`IntradayService`** — Timer-based per-stock intraday fetch (60s trading / 300s off-hours).
- **`HotkeyService`** — Win32 `RegisterHotKey`/`UnregisterHotKey` P/Invoke, `HwndSource.AddHook()` for `WM_HOTKEY`.
- **`TradingHours`** — A-share (9:30–11:30, 13:00–15:00) and HK (9:30–12:00, 13:00–16:00) sessions, weekend exclusion, dynamic interval scaling.

### Detail Panel Interaction

- **`MainViewModel.SelectedStock`** (StockItem?) — drives the bottom detail panel visibility.
- **Click a stock row** → sets `SelectedStock` to that stock; bottom panel appears.
- **Click the same stock again** → sets `SelectedStock` to null; panel hides.
- **Click a different stock** → switches `SelectedStock` and panel content updates.
- **K-line loading** — triggered on first expansion (`KlineData.Count == 0`). Fetches OHLCV data via `EastMoneyClient.GetKlineAsync()`. Period switching (5分/30分/日线/周线) clears cached data and re-fetches.
- Drag-drop and click are distinguished by tracking mouse movement distance against `SystemParameters.MinimumHorizontalDragDistance`.

---

## 7. Configuration

### AppConfig (`%AppData%/StockTool/config.json`)

```jsonc
{
  "watchlist": [
    {
      "name": "招商银行",
      "code": "SH600036",
      "market": "沪A",
      "customName": ""
    }
  ],
  "windowLeft": null,
  "windowTop": null,
  "windowWidth": 420,
  "windowHeight": 600,
  "opacity": 0.9,
  "fontSize": 14,
  "refreshInterval": 1,
  "hotkey": "Ctrl+Shift+S",
  "topmost": true,
  "showMarketTag": true
}
```

### Default Settings

| Setting | Default | Range |
|---------|---------|-------|
| Window size | 420 × 600 | Min 320 × 300 |
| Opacity | 0.9 (90%) | 0.0–1.0 |
| Font size | 14 | 12–20 |
| Refresh interval | 1 second | 1–5s (auto-scaled off-hours) |
| Hotkey | `Ctrl+Shift+S` | Any Ctrl/Alt/Shift/Win + letter/digit/F-key |
| Topmost | Enabled | Boolean |
| Show market tag | Enabled | Boolean |

### Default Watchlist

| Display Name | Code | Market |
|-------------|------|--------|
| 招商银行 | `SH600036` | 沪A |
| 中国平安 | `SH601318` | 沪A |
| 小米集团 | `HK01810` | 港股 |
| 新华保险 | `SH601336` | 沪A |
| 海尔智家 | `SH600690` | 沪A |
| 沪深300ETF | `SH510300` | ETF |
| 黄金股ETF | `SH159562` | ETF |
