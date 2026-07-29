# 盯盘 (StockTool) — 项目说明

## 1. 项目概述

**盯盘** 是一款基于 **WPF + .NET 9** 的实时股票盯盘桌面小工具。提供轻量、可置顶的浮窗，展示用户自选股的实时行情，覆盖 A 股（沪/深）、港股与 A 股 ETF。数据来自 **东方财富** 公开 HTTP 接口，无需 API Key。

面向需要一眼扫盘、不打扰日常工作的用户：支持系统托盘与全局热键显隐窗口。

| 属性 | 说明 |
|------|------|
| 技术栈 | WPF + .NET 9 + Windows Forms（托盘 NotifyIcon） |
| 语言 | C# 13 |
| 目标系统 | Windows 10/11 |
| UI 框架 | WPF，`AllowsTransparency`，无边框 |
| 数据源 | 东方财富公开 HTTP 接口 |
| 输出类型 | `WinExe`（独立桌面应用） |

---

## 2. 架构

解决方案按职责分为三个项目：

```
StockTool.slnx
└── /src/
    ├── StockTool.Core/      # 模型、配置存储（无 UI、无网络依赖）
    ├── StockTool.Data/      # 东方财富 HTTP 客户端与接口解析
    └── StockTool/           # WPF 界面、ViewModel、服务、控件、转换器
```

### 依赖关系

```
StockTool (WPF 应用, net9.0-windows)
  ├── 引用 StockTool.Core (net9.0)
  └── 引用 StockTool.Data (net9.0)

StockTool.Data (net9.0)
  └── 引用 StockTool.Core (net9.0)

StockTool.Core (net9.0)
  （无项目依赖 — 纯模型 + 配置）
```

### StockTool.Core — 模型与配置

- **`AppConfig.cs`** — 配置 POCO：自选列表、窗口位置/尺寸、透明度、字号、刷新间隔、热键、置顶、是否显示市场标签。内置默认值（透明度 0.9、字号 14、1 秒刷新、`Ctrl+Shift+S`、置顶开启）。
- **`WatchlistEntry.cs`** — 自选条目：`Name`、`Code`（内部格式如 `SH600036`）、`Market`（`沪A`/`深A`/`港股`/`ETF`）、可选 `CustomName` 别名。
- **`QuoteData.cs`** — 东财 JSON 反序列化 DTO：批量行情（`QuoteBatchResponse` → `QuoteItem`，F2=现价、F3=涨跌幅、F17=昨收等）、分时（`IntradayResponse`）、搜索建议（`SearchResponse` → `SearchItem`）。
- **`StockItem.cs`** — 列表行可观察实体：`Name`、`CustomName`、`DisplayName`（计算属性）、`Code`、`CodeNumeric`、`Market`、`CurrentPrice`、`ChangePercent`、`YesterdayClose`、`IntradayPoints`。实现 `INotifyPropertyChanged`。
- **`ConfigStore.cs`** — JSON 持久化，读写 `%AppData%/StockTool/config.json`（`System.Text.Json`，camelCase，缩进）。文件缺失或损坏时回退默认配置。

### StockTool.Data — API 客户端

- **`EastMoneyClient.cs`** — 唯一对外 HTTP 通信类。
  - 复用单个 `HttpClient`，超时 8 秒，浏览器风格 User-Agent。
  - **`GetBatchQuotesAsync(codes)`** — `push2.eastmoney.com/api/qt/ulist.np/get`，逗号分隔 `secid`，一次返回全部自选现价、涨跌幅、昨收、名称。
  - **`GetIntradayAsync(code)`** — `push2his.eastmoney.com/api/qt/stock/trends2/get`，当日分钟级分时；解析 CSV 行，取第二字段为价格。
  - **`SearchAsync(keyword)`** — `searchapi.eastmoney.com/api/suggest/get`，`type=14`，供添加自选搜索，最多约 10 条。
  - 静态辅助：`ToSecId()` 将 `SH600036` 映射为东财 `1.600036`；`InternalCodeFromSecId()` 反向；`MarketLabelFromPrefix()`：`SH`→`沪A`，`SZ`→`深A`，`HK`→`港股`。

### StockTool — WPF 主程序

包含全部 UI、ViewModel、WPF 服务、自定义控件与值转换器。

---

## 3. 功能列表

### 已实现（MVP 各阶段完成）

| # | 功能 | 说明 |
|---|------|------|
| 1 | **自选管理** | 搜索对话框添加；右键菜单删除、重命名、上移/下移 |
| 2 | **实时行情** | 按可配置 1–5 秒批量轮询东财接口 |
| 3 | **分时 Sparkline** | 自定义 `SparklineControl`（`OnRender` / `StreamGeometry`）绘制分钟走势 |
| 4 | **多市场** | A 股（沪/深）、港股、A 股 ETF；统一内部代码前缀 `SH`/`SZ`/`HK` |
| 5 | **红涨绿跌** | 上涨 `#D93025`，下跌 `#1A8C3F`，平盘灰色 |
| 6 | **无边框置顶窗** | `WindowStyle=None`，`AllowsTransparency=True`，`Topmost` 可绑；标题栏 `DragMove` |
| 7 | **背景透明度** | 设置页 0–100% 滑条；`OpacityToBrushConverter` 作用于外层 Border |
| 8 | **字体大小** | 设置页 12–20；`FontSizeConverter` 按元素偏移应用 |
| 9 | **刷新间隔** | 行情 1–5 秒；休市约 5 倍降频（最低 10 秒）；分时交易中约 60 秒 / 休市约 300 秒 |
| 10 | **全局热键** | 默认 `Ctrl+Shift+S` 显隐；Win32 `RegisterHotKey`；设置页可录制改键 |
| 11 | **系统托盘** | `NotifyIcon`：显示 / 设置 / 退出；双击恢复；关窗隐藏到托盘不退出 |
| 12 | **配置持久化** | 自选、窗口位置尺寸、透明度、字号、热键等写入 `config.json` |
| 13 | **市场标签** | 可选彩色徽章（沪A/深A/港股/ETF），设置中开关 |
| 14 | **交易时段感知** | `TradingHours`：A 股 9:30–11:30、13:00–15:00；港股 9:30–12:00、13:00–16:00；周末排除，动态调整刷新 |
| 15 | **自定义名称** | 右键重命名，优先显示别名 |
| 16 | **窗口可调大小** | `CanResizeWithGrip`，最小 320×300；位置尺寸持久化 |
| 17 | **空状态** | 无自选时提示「暂无自选股，点击 + 添加」 |
| 18 | **搜索添加** | 300ms 防抖联想；展示名称、代码、市场；点击行或 `+` 添加 |
| 19 | **默认自选** | 首次启动 7 只：招商银行、中国平安、小米集团、新华保险、海尔智家、沪深300ETF、黄金股ETF |

---

## 4. UI 组件

### 主窗口

圆角（8px）无边框浮窗，纵向三区：

```
┌─────────────────────────────────────────────────┐
│  盯盘                    🔍  ⚙  −  ✕          │  ← 标题栏 (36px)
├─────────────────────────────────────────────────┤
│  招商银行          ┌──────────┐   36.50         │
│  [沪A] 600036      │  分时图  │   +1.23%        │  ← 股票行 (52px)
│                    └──────────┘                 │
│  中国平安          ┌──────────┐   48.20         │
│  [沪A] 601318      │  分时图  │   -0.45%        │
│                    └──────────┘                 │
│  ...                                           │
├─────────────────────────────────────────────────┤
│  ● 数据来源：东方财富 | 仅供参考                  │  ← 状态栏 (24px)
└─────────────────────────────────────────────────┘
```

**视觉要点：**

- **标题栏** — 半透明白底。按钮：搜索/添加、设置、隐藏到托盘、退出（关闭按钮悬停变红）。
- **股票行** — 三列：左（约 120px）粗体名称 + 市场徽章 + 代码；中分时；右（约 100px）现价 + 涨跌幅。
- **右键菜单** — 股票标题，然后：上移、下移、重命名、删除（红色）。
- **配色** — 浅色：正文 `#1A1A1A`，次要 `#888888`，涨红 `#D93025`，跌绿 `#1A8C3F`。

### 添加自选对话框

- 约 400×440，相对主窗居中，圆角 12px。
- 胶囊形搜索框，带搜索图标与清空按钮。
- 300ms 防抖 + `CancellationTokenSource` 取消过期请求。
- 结果：粗体名称 + 代码 + 市场；圆形 `+`；整行可点。
- 选中态：背景 `#E6F0FA`，边框 `#B0D0F0`。

### 设置窗口

- 宽约 340px，高度自适应，相对主窗居中。
- 控件：透明度滑条（0–100%）、字号下拉（12–20）、刷新间隔（1–5）、热键文本 + 录制按钮、置顶复选、市场标签复选。
- 输入圆角 6px；保存按钮蓝色 `#2563EB`，取消灰色。

### 重命名对话框

- 代码创建，约 320×160，居中、置顶。
- 圆角输入框；确定蓝色、取消灰色。

---

## 5. 目录结构

```
D:/Code/Demo/StockTool/
├── StockTool.slnx                          # 解决方案
├── docs/
│   ├── PROJECT_SUMMARY.md                  # 英文项目摘要
│   └── 项目说明.md                          # 本文档（中文）
├── publish/                                # 发布输出
└── src/
    ├── StockTool.Core/
    │   ├── Models/
    │   │   ├── AppConfig.cs                # 配置 POCO + WatchlistEntry
    │   │   ├── QuoteData.cs                # API 响应 DTO
    │   │   └── StockItem.cs                # 可观察股票行实体
    │   └── Services/
    │       └── ConfigStore.cs              # JSON 持久化
    │
    ├── StockTool.Data/
    │   └── EastMoneyClient.cs              # 东财三接口 HTTP 客户端
    │
    └── StockTool/
        ├── App.xaml / App.xaml.cs           # 资源、托盘、生命周期
        ├── MainWindow.xaml / .xaml.cs       # 主窗布局与逻辑
        ├── Controls/
        │   └── SparklineControl.cs          # 分时自绘控件
        ├── Converters/
        │   ├── FontSizeConverter.cs          # 基准字号 + 偏移
        │   ├── NegativeToBooleanConverter.cs # decimal < 0 → true
        │   ├── OpacityToBrushConverter.cs    # 透明度 → 带 Alpha 画刷
        │   └── ZeroToVisibleConverter.cs     # count==0 → Visible
        ├── Services/
        │   ├── HotkeyService.cs             # Win32 全局热键
        │   ├── IntradayService.cs           # 分时拉取定时器
        │   ├── QuoteService.cs              # 批量行情轮询
        │   └── TradingHours.cs              # 交易时段判断
        ├── Themes/
        │   └── Generic.xaml                 # Sparkline 默认样式
        ├── ViewModels/
        │   └── MainViewModel.cs             # 主 DataContext
        └── Views/
            ├── AddStockDialog.xaml / .cs     # 添加自选
            └── SettingsWindow.xaml / .cs     # 设置
```

---

## 6. 关键技术细节

### 接口（东方财富 — 无需鉴权）

| 能力 | 端点 | 用途 | 调用方 |
|------|------|------|--------|
| 批量行情 | `push2.eastmoney.com/api/qt/ulist.np/get` | 现价、涨跌幅、名称、昨收 | `QuoteService`（1–5s） |
| 当日分时 | `push2his.eastmoney.com/api/qt/stock/trends2/get` | 分钟级价格点 | `IntradayService`（60–300s） |
| 搜索联想 | `searchapi.eastmoney.com/api/suggest/get` | 代码/名称补全 | `AddStockDialog`（300ms 防抖） |

**内部代码格式**：`{市场前缀}{数字代码}`（如 `SH600036`）。市场 ID：1=SH，0=SZ，116=HK。

### 值转换器

| 转换器 | 用途 |
|--------|------|
| `FontSizeConverter` | 基准字号 + 偏移 |
| `NegativeToBooleanConverter` | decimal < 0 → true（驱动跌色触发器） |
| `OpacityToBrushConverter` | 0.0–1.0 → 带 Alpha 画刷；可选十六进制颜色参数 |
| `ZeroToVisibleConverter` | count==0 → Visible（空状态） |

### 服务

- **`QuoteService`** — `DispatcherTimer` 批量拉行情，经 Dispatcher 写回 `StockItem`；网络失败静默保留上次数据。
- **`IntradayService`** — 按股定时拉分时。
- **`HotkeyService`** — `RegisterHotKey` / `UnregisterHotKey`，`HwndSource.AddHook` 处理 `WM_HOTKEY`。
- **`TradingHours`** — A 股 / 港股时段与周末判断，动态缩放刷新间隔。

---

## 7. 配置

### AppConfig（`%AppData%/StockTool/config.json`）

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

### 默认设置

| 项 | 默认 | 范围 |
|----|------|------|
| 窗口尺寸 | 420 × 600 | 最小 320 × 300 |
| 透明度 | 0.9（90%） | 0.0–1.0 |
| 字号 | 14 | 12–20 |
| 刷新间隔 | 1 秒 | 1–5s（休市自动降频） |
| 热键 | `Ctrl+Shift+S` | Ctrl/Alt/Shift/Win + 字母/数字/F 键 |
| 置顶 | 开启 | 布尔 |
| 显示市场标签 | 开启 | 布尔 |

### 默认自选

| 显示名 | 代码 | 市场 |
|--------|------|------|
| 招商银行 | `SH600036` | 沪A |
| 中国平安 | `SH601318` | 沪A |
| 小米集团 | `HK01810` | 港股 |
| 新华保险 | `SH601336` | 沪A |
| 海尔智家 | `SH600690` | 沪A |
| 沪深300ETF | `SH510300` | ETF |
| 黄金股ETF | `SH159562` | ETF |

---

## 8. 说明与合规

- 数据来自非官方公开接口，仅供个人盯盘参考，**不构成投资建议**。
- 「秒级」为 HTTP 轮询近似实时，可能存在延迟，非交易所官方推送。
