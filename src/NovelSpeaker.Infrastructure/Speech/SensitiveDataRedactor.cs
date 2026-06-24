using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovelSpeaker.Infrastructure.Speech;

internal static partial class SensitiveDataRedactor
{
    public static string RedactUrl(string url)
    {
        var separatorIndex = url.IndexOf('?', StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex == url.Length - 1)
        {
            return RedactPlainText(url);
        }

        var prefix = url[..(separatorIndex + 1)];
        var query = url[(separatorIndex + 1)..];
        var redactedPairs = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var equalsIndex = pair.IndexOf('=', StringComparison.Ordinal);
                if (equalsIndex < 0)
                {
                    return pair;
                }

                var key = pair[..equalsIndex];
                return IsSensitiveKey(key)
                    ? $"{key}=***"
                    : pair;
            });

        return RedactPlainText(prefix + string.Join("&", redactedPairs));
    }

    public static string? RedactJsonLikeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            WriteRedactedElement(writer, null, document.RootElement);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return RedactPlainText(value);
        }
    }

    public static string RedactPlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = BearerTokenPattern().Replace(value, "$1***");
        redacted = SensitiveAssignmentPattern().Replace(redacted, match =>
        {
            var key = match.Groups["key"].Value;
            var separator = match.Groups["separator"].Value;
            return $"{key}{separator}***";
        });

        return redacted;
    }

    public static string? SerializeRedactedDictionary(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var ordered = values
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                pair => pair.Key,
                pair => IsSensitiveKey(pair.Key) ? "***" : pair.Value,
                StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(ordered);
    }

    public static bool IsSensitiveKey(string key)
    {
        return key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("login", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("apikey", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteRedactedElement(Utf8JsonWriter writer, string? propertyName, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteRedactedElement(writer, property.Name, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteRedactedElement(writer, propertyName, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String when propertyName is not null && IsSensitiveKey(propertyName):
                writer.WriteStringValue("***");
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    [GeneratedRegex(@"(?i)(Bearer\s+)[^\s,""]+")]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"(?im)(?<key>[A-Za-z0-9_\-\.]*(authorization|cookie|token|secret|password|login|api[_\-]?key)[A-Za-z0-9_\-\.]*)\s*(?<separator>[:=])\s*[^\r\n&;, ]+")]
    private static partial Regex SensitiveAssignmentPattern();
}
