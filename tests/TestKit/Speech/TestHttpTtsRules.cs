using System.Text.Json;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.TestKit.Speech;

internal static class TestHttpTtsRules
{
    public static HttpTtsRule Create(
        long id,
        string name,
        string url,
        string? contentType,
        string? concurrentRate,
        string? headerJson,
        string? requestOptionsJson,
        long? lastUpdateTime,
        bool isEnabled,
        string? lastUsedAt,
        string createdAt,
        string updatedAt)
    {
        var body = ReadRequestBody(requestOptionsJson);
        return new HttpTtsRule(
            id,
            name,
            url,
            contentType,
            concurrentRate,
            ParseHeaders(headerJson),
            ReadRequestOption(requestOptionsJson, "method", preserveRawJson: false),
            body.Text,
            body.IsJsonStructure,
            lastUpdateTime,
            isEnabled,
            ParseOptionalDate(lastUsedAt),
            ParseDate(createdAt),
            ParseDate(updatedAt));
    }

    public static NormalizedHttpTtsRule Normalize(this HttpTtsRule rule) =>
        new TtsRuleNormalizer().Normalize(rule);

    private static IReadOnlyDictionary<string, string> ParseHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(
            item => item.Name,
            item => item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() ?? string.Empty : item.Value.GetRawText(),
            StringComparer.OrdinalIgnoreCase);
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

    private static TestRequestBody ReadRequestBody(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TestRequestBody(null, false);
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("body", out var value))
        {
            return new TestRequestBody(null, false);
        }

        return value.ValueKind == JsonValueKind.String
            ? new TestRequestBody(value.GetString(), false)
            : new TestRequestBody(value.GetRawText(), true);
    }

    private static DateTimeOffset? ParseOptionalDate(string? value) =>
        value is null ? null : ParseDate(value);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UnixEpoch;

    private sealed record TestRequestBody(string? Text, bool IsJsonStructure);
}
