using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockTool.Core.Models;

/// <summary>东财字段常为数字，停牌/无效时可能是 "-" 或空字符串。</summary>
public sealed class FlexibleDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetDecimal(out var d) ? d : 0m;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s) || s == "-") return 0m;
                return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
            case JsonTokenType.Null:
                return 0m;
            default:
                reader.Skip();
                return 0m;
        }
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

public sealed class FlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var i) ? i : 0;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s) || s == "-") return 0;
                return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
            case JsonTokenType.Null:
                return 0;
            default:
                reader.Skip();
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

/// <summary>东财 diff 在仅 1 条时可能是对象而非数组。</summary>
public sealed class SingleOrArrayConverter<T> : JsonConverter<List<T>>
{
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.StartArray)
            return JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? [];

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var item = JsonSerializer.Deserialize<T>(ref reader, options);
            return item is null ? [] : [item];
        }

        reader.Skip();
        return [];
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}
