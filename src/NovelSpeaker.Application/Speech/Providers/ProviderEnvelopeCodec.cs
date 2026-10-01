using System.Text.Json;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public sealed record ProviderEnvelopeItem(
    int Index,
    string? Name,
    HttpSpeechProviderConfiguration? Configuration,
    string? Error)
{
    public bool IsValid => Error is null && Configuration is not null;
}

public sealed record ProviderEnvelopeReadResult(
    IReadOnlyList<ProviderEnvelopeItem> Items,
    string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>Reads and writes versioned, portable NovelSpeaker Provider documents.</summary>
public static class ProviderEnvelopeCodec
{
    public const int SchemaVersion = 1;

    public static ProviderEnvelopeReadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProviderEnvelopeReadResult([], "Provider 文件不能为空。");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var schemaVersion) ||
                schemaVersion != SchemaVersion ||
                !root.TryGetProperty("providers", out var providers) ||
                providers.ValueKind != JsonValueKind.Array)
            {
                return new ProviderEnvelopeReadResult([], "Provider 文件版本或结构不受支持。");
            }

            var items = new List<ProviderEnvelopeItem>();
            var index = 0;
            foreach (var item in providers.EnumerateArray())
            {
                items.Add(ReadItem(index++, item));
            }

            return items.Count == 0
                ? new ProviderEnvelopeReadResult([], "Provider 文件没有可导入条目。")
                : new ProviderEnvelopeReadResult(items, null);
        }
        catch (JsonException)
        {
            return new ProviderEnvelopeReadResult([], "Provider 文件不是有效的 JSON。");
        }
    }

    public static string Write(SpeechProviderInstance provider) => Write([provider]);

    public static string Write(IReadOnlyList<SpeechProviderInstance> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (providers.Count == 0 || providers.Any(provider =>
                provider.Configuration is not HttpSpeechProviderConfiguration ||
                !ProviderConfigurationValidator.Validate(provider).IsValid))
        {
            throw new InvalidOperationException("只能导出已配置的 HTTP Provider。");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteStartArray("providers");
            foreach (var provider in providers)
            {
                var http = (HttpSpeechProviderConfiguration)provider.Configuration;
                writer.WriteStartObject();
                writer.WriteString("providerType", "http");
                writer.WriteString("name", provider.Name);
                writer.WriteStartObject("configuration");
                writer.WriteString("urlTemplate", http.UrlTemplate);
                writer.WriteString("method", http.Method);
                writer.WriteStartObject("headers");
                foreach (var header in http.Headers.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    writer.WriteString(header.Key, header.Value);
                }

                writer.WriteEndObject();
                if (http.BodyTemplate is not null)
                {
                    writer.WriteString("bodyTemplate", http.BodyTemplate);
                }

                if (http.RateLimit is { } limit)
                {
                    writer.WriteStartObject("rateLimit");
                    writer.WriteNumber("maxRequests", limit.MaxRequests);
                    writer.WriteNumber("windowMilliseconds", limit.WindowMilliseconds);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static ProviderEnvelopeItem ReadItem(int index, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !TryString(item, "providerType", out var type))
        {
            return new ProviderEnvelopeItem(index, null, null, "Provider 条目缺少类型。");
        }

        if (!string.Equals(type, "http", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderEnvelopeItem(index, null, null, "Provider 类型不受支持。");
        }

        if (!TryString(item, "name", out var name) ||
            !item.TryGetProperty("configuration", out var configuration) ||
            configuration.ValueKind != JsonValueKind.Object ||
            !TryString(configuration, "urlTemplate", out var url) ||
            !TryString(configuration, "method", out var method) ||
            !configuration.TryGetProperty("headers", out var headerObject) ||
            headerObject.ValueKind != JsonValueKind.Object)
        {
            return new ProviderEnvelopeItem(index, null, null, "HTTP Provider 配置不完整。");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headerObject.EnumerateObject())
        {
            if (header.Value.ValueKind != JsonValueKind.String ||
                !headers.TryAdd(header.Name, header.Value.GetString()!))
            {
                return new ProviderEnvelopeItem(index, name, null, "HTTP Provider Header 无效或重复。");
            }
        }

        string? body = null;
        if (configuration.TryGetProperty("bodyTemplate", out var bodyValue))
        {
            if (bodyValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
            {
                return new ProviderEnvelopeItem(index, name, null, "HTTP Provider Body 必须是模板文本。");
            }

            body = bodyValue.ValueKind == JsonValueKind.String ? bodyValue.GetString() : null;
        }

        ProviderRequestRateLimit? rateLimit = null;
        if (configuration.TryGetProperty("rateLimit", out var rateValue) &&
            rateValue.ValueKind != JsonValueKind.Null)
        {
            if (rateValue.ValueKind != JsonValueKind.Object ||
                !rateValue.TryGetProperty("maxRequests", out var max) ||
                max.ValueKind != JsonValueKind.Number ||
                !max.TryGetInt32(out var maxRequests) ||
                !rateValue.TryGetProperty("windowMilliseconds", out var window) ||
                window.ValueKind != JsonValueKind.Number ||
                !window.TryGetInt32(out var windowMilliseconds))
            {
                return new ProviderEnvelopeItem(index, name, null, "HTTP Provider 请求频率限制无效。");
            }

            rateLimit = new ProviderRequestRateLimit(maxRequests, windowMilliseconds);
        }

        var normalizedName = name!.Trim();
        var http = new HttpSpeechProviderConfiguration(url!, method!.Trim().ToUpperInvariant(), headers, body, rateLimit);
        var candidate = new SpeechProviderInstance(
            ProviderId.New(), normalizedName, 0, http, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var validation = ProviderConfigurationValidator.Validate(candidate);
        return validation.IsValid
            ? new ProviderEnvelopeItem(index, normalizedName, http, null)
            : new ProviderEnvelopeItem(index, normalizedName, null, string.Join(" ", validation.Errors));
    }

    private static bool TryString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }
}
