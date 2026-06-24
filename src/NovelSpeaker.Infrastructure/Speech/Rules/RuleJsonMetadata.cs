using System.Text.Json;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal sealed record RuleJsonMetadata(
    string Url,
    string? ContentType,
    string? ConcurrentRate,
    string? Header,
    string? RequestOptionsJson,
    bool EnabledCookieJar,
    long? LastUpdateTime)
{
    public static RuleJsonMetadata Parse(string ruleJson)
    {
        using var document = JsonDocument.Parse(ruleJson);
        var root = document.RootElement;

        return new RuleJsonMetadata(
            ReadOptionalString(root, "url") ?? string.Empty,
            ReadOptionalString(root, "contentType"),
            ReadOptionalString(root, "concurrentRate"),
            ReadOptionalString(root, "header"),
            ReadOptionalJson(root, "requestOptions"),
            ReadOptionalBoolean(root, "enabledCookieJar"),
            ReadOptionalInt64(root, "lastUpdateTime"));
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            _ => value.ToString()
        };
    }

    private static string? ReadOptionalJson(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out var value)
            ? value.GetRawText()
            : null;
    }

    private static bool ReadOptionalBoolean(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.String &&
               bool.TryParse(value.GetString(), out var parsed) &&
               parsed;
    }

    private static long? ReadOptionalInt64(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
