using System.Text.Encodings.Web;
using System.Text.Json;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal static class TtsRulePersistenceMapper
{
    public static TtsRuleRow FromDomain(HttpTtsRule rule) => new(
        rule.Id,
        rule.Name,
        rule.Url,
        rule.ContentType,
        rule.ConcurrentRate,
        SerializeHeaders(rule.Headers),
        SerializeRequestOptions(rule.RequestMethod, rule.RequestBody, rule.RequestBodyIsJsonStructure),
        rule.LastUpdateTime,
        rule.IsEnabled,
        rule.LastUsedAt is null ? null : SqliteDateTimeMapper.Format(rule.LastUsedAt.Value),
        SqliteDateTimeMapper.Format(rule.CreatedAt),
        SqliteDateTimeMapper.Format(rule.UpdatedAt));

    public static HttpTtsRule ToDomain(TtsRuleRow row) => new(
        row.Id,
        row.Name,
        row.Url,
        row.ContentType,
        row.ConcurrentRate,
        ParseHeaders(row.Header),
        ParseRequestMethod(row.RequestOptionsJson),
        ParseRequestBody(row.RequestOptionsJson),
        IsRequestBodyJsonStructure(row.RequestOptionsJson),
        row.LastUpdateTime,
        row.IsEnabled,
        row.LastUsedAt is null ? null : SqliteDateTimeMapper.Parse(row.LastUsedAt),
        SqliteDateTimeMapper.Parse(row.CreatedAt),
        SqliteDateTimeMapper.Parse(row.UpdatedAt));

    public static IReadOnlyDictionary<string, string> ParseHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText(),
            StringComparer.OrdinalIgnoreCase);
    }

    public static string? SerializeHeaders(IReadOnlyDictionary<string, string> headers) =>
        headers.Count == 0 ? null : JsonSerializer.Serialize(headers);

    public static string? ParseRequestMethod(string? json) => ReadRequestOption(json, "method", preserveRawJson: false);

    public static string? ParseRequestBody(string? json) => ReadRequestBody(json)?.Text;

    public static bool IsRequestBodyJsonStructure(string? json) => ReadRequestBody(json)?.IsJsonStructure ?? false;

    public static string? SerializeRequestOptions(string? method, string? body, bool bodyIsJsonStructure)
    {
        if (string.IsNullOrWhiteSpace(method) && string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(method))
        {
            writer.WriteString("method", method);
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            writer.WritePropertyName("body");
            if (bodyIsJsonStructure)
            {
                using var bodyDocument = JsonDocument.Parse(body);
                bodyDocument.RootElement.WriteTo(writer);
            }
            else
            {
                writer.WriteStringValue(body);
            }
        }

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? ReadRequestOption(string? json, string name, bool preserveRawJson)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(name, out var value))
        {
            return null;
        }

        return preserveRawJson || value.ValueKind != JsonValueKind.String
            ? value.GetRawText()
            : value.GetString();
    }

    private static ParsedRequestBody? ReadRequestBody(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("body", out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? new ParsedRequestBody(value.GetString() ?? string.Empty, false)
            : new ParsedRequestBody(value.GetRawText(), true);
    }

    private sealed record ParsedRequestBody(string Text, bool IsJsonStructure);
}
