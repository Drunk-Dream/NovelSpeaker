using System.Text.Json;

namespace NovelSpeaker.Infrastructure.Persistence.Speech;

internal static class LegacyHttpRuleFieldsCodec
{
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

    public static string? ParseRequestMethod(string? json) => ReadRequestOption(json, "method");

    public static string? ParseRequestBody(string? json) => ReadRequestBody(json)?.Text;

    public static bool IsRequestBodyJsonStructure(string? json) => ReadRequestBody(json)?.IsJsonStructure ?? false;

    private static string? ReadRequestOption(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;
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
