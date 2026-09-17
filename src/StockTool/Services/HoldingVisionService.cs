using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace StockTool.Services;

public sealed class ParsedHoldingRow
{
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public decimal Shares { get; init; }
    public decimal Cost { get; init; }
    public bool IsHk { get; init; }
}

public sealed class HoldingVisionOptions
{
    public string Provider { get; init; } = "deepseek";
    public string ApiKey { get; init; } = "";
}

// 通过 DeepSeek / 小米 MiMo 识图解析持仓截图
public static class HoldingVisionService
{
    private static readonly HttpClient Http = CreateHttp();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string StockPrompt = """
        你是证券持仓截图识别助手。请识别图片中「持仓股」列表的每一只股票，忽略顶部资产汇总、表头和底部导航。
        每只股票提取：
        - name: 完整股票名称（如 招商银行、小米集团-W、新华保险）
        - code: 若图中可见代码则填写，否则空字符串
        - shares: 持仓数量（持仓/可用列上方数字，不是可用数量）
        - cost: 成本价（成本/现价列上方数字；去掉 HK$、逗号等前缀与千分位）
        - isHk: 港股为 true（名称含 -W、有沪港标识、或成本带 HK$）
        只输出 JSON 数组，不要其它文字或 Markdown。示例：
        [{"name":"招商银行","code":"","shares":1400,"cost":35.083,"isHk":false},{"name":"小米集团-W","code":"01810","shares":1800,"cost":29.654,"isHk":true}]
        """;

    private const string FundPrompt = """
        你是基金持仓详情截图识别助手。请识别支付宝/基金 App 的基金持仓详情页（可含多只，通常为一只）。
        每只基金提取：
        - name: 基金完整名称（如 南方半导体产业股票发起C）
        - code: 6 位基金代码（如 020554，名称下方）
        - shares: 「持有份额」数值（不是可用份额）
        - cost: 「持仓成本单价」数值
        - isHk: 固定 false
        忽略总金额、昨日收益、持有收益、最新净值等其它字段。
        只输出 JSON 数组，不要其它文字或 Markdown。示例：
        [{"name":"南方半导体产业股票发起C","code":"020554","shares":783.88,"cost":2.9979,"isHk":false}]
        """;

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    public static Task<(List<ParsedHoldingRow> Rows, string? Error)> RecognizeAsync(
        BitmapSource image,
        HoldingVisionOptions options,
        CancellationToken ct = default)
        => RecognizeAsync(image, options, isFund: false, ct);

    // 识别持仓截图；isFund=true 时按基金详情页提取代码/持有份额/成本单价
    public static async Task<(List<ParsedHoldingRow> Rows, string? Error)> RecognizeAsync(
        BitmapSource image,
        HoldingVisionOptions options,
        bool isFund,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return ([], "请先在设置中填写 API Key");

        string provider = (options.Provider ?? "deepseek").Trim().ToLowerInvariant();
        string endpoint;
        string model;
        bool useApiKeyHeader;

        switch (provider)
        {
            case "mimo":
                endpoint = "https://api.xiaomimimo.com/v1/chat/completions";
                model = "mimo-v2.5";
                useApiKeyHeader = true;
                break;
            default:
                endpoint = "https://api.deepseek.com/chat/completions";
                model = "deepseek-flash";
                useApiKeyHeader = false;
                break;
        }

        string dataUrl;
        try
        {
            dataUrl = ToPngDataUrl(image);
        }
        catch (Exception ex)
        {
            return ([], $"图片编码失败：{ex.Message}");
        }

        string prompt = isFund ? FundPrompt : StockPrompt;
        var body = new
        {
            model,
            temperature = 0.1,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = dataUrl }
                        }
                    }
                }
            }
        };

        string json = JsonSerializer.Serialize(body);
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (useApiKeyHeader)
        {
            req.Headers.TryAddWithoutValidation("api-key", options.ApiKey.Trim());
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        }
        else
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        }

        string responseText;
        try
        {
            using var resp = await Http.SendAsync(req, ct);
            responseText = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return ([], $"识别接口失败 ({(int)resp.StatusCode})：{TrimError(responseText)}");
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            return ([], "识别已取消");
        }
        catch (Exception ex)
        {
            return ([], $"识别请求失败：{ex.Message}");
        }

        string? content;
        try
        {
            content = ExtractMessageContent(responseText);
        }
        catch (Exception ex)
        {
            return ([], $"解析接口响应失败：{ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(content))
            return ([], "模型未返回识别结果");

        try
        {
            var rows = ParseRows(content);
            if (rows.Count == 0)
                return ([], isFund
                    ? "未识别到基金持仓，请确认粘贴的是基金详情截图"
                    : "未识别到持仓行，请确认粘贴的是持仓列表截图");
            return (rows, null);
        }
        catch (Exception ex)
        {
            return ([], $"解析识别结果失败：{ex.Message}");
        }
    }

    private static string ToPngDataUrl(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        string b64 = Convert.ToBase64String(ms.ToArray());
        return $"data:image/png;base64,{b64}";
    }

    private static string? ExtractMessageContent(string responseText)
    {
        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;
        var message = choices[0].GetProperty("message");
        if (message.TryGetProperty("content", out var contentEl))
        {
            if (contentEl.ValueKind == JsonValueKind.String)
                return contentEl.GetString();
            // 部分接口 content 为数组
            if (contentEl.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in contentEl.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var t))
                        sb.Append(t.GetString());
                    else if (part.ValueKind == JsonValueKind.String)
                        sb.Append(part.GetString());
                }
                return sb.ToString();
            }
        }
        return null;
    }

    private static List<ParsedHoldingRow> ParseRows(string content)
    {
        string json = ExtractJsonArray(content);
        var items = JsonSerializer.Deserialize<List<VisionHoldingDto>>(json, JsonOpts) ?? [];
        var rows = new List<ParsedHoldingRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            string name = (item.Name ?? "").Trim();
            if (name.Length < 2) continue;
            if (item.Shares <= 0 || item.Cost <= 0) continue;
            if (!seen.Add(name)) continue;

            bool isHk = item.IsHk
                || name.Contains("-W", StringComparison.OrdinalIgnoreCase)
                || name.Contains("－W", StringComparison.OrdinalIgnoreCase);

            rows.Add(new ParsedHoldingRow
            {
                Name = name,
                Code = (item.Code ?? "").Trim(),
                Shares = item.Shares,
                Cost = item.Cost,
                IsHk = isHk
            });
        }

        return rows;
    }

    private static string ExtractJsonArray(string content)
    {
        string text = content.Trim();
        // 去掉 ```json ... ```
        var fence = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (fence.Success)
            text = fence.Groups[1].Value.Trim();

        int start = text.IndexOf('[');
        int end = text.LastIndexOf(']');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("未找到 JSON 数组");
        return text[start..(end + 1)];
    }

    private static string TrimError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "无详情";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? body;
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString() ?? body;
            }
        }
        catch
        {
            // ignore
        }
        return body.Length > 200 ? body[..200] + "…" : body;
    }

    private sealed class VisionHoldingDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("shares")]
        public decimal Shares { get; set; }

        [JsonPropertyName("cost")]
        public decimal Cost { get; set; }

        [JsonPropertyName("isHk")]
        public bool IsHk { get; set; }
    }
}
