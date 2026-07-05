using System.Text.Json;
using System.Text.Encodings.Web;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal sealed record RuleJsonMetadata(
    string Url,
    string? ContentType,
    string? ConcurrentRate,
    string? Header,
    string? RequestOptionsJson,
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
            NormalizeRequestOptionsJson(ReadOptionalJson(root, "requestOptions")),
            ReadOptionalInt64(root, "lastUpdateTime"));
    }

    private static string? NormalizeRequestOptionsJson(string? requestOptionsJson)
    {
        if (string.IsNullOrWhiteSpace(requestOptionsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(requestOptionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? method = null;
            string? body = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "method":
                        method = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.GetRawText();
                        break;
                    case "body":
                        body = property.Value.GetRawText();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(method) && string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            writer.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(method))
            {
                writer.WriteString("method", method);
            }

            if (!string.IsNullOrWhiteSpace(body))
            {
                writer.WritePropertyName("body");
                using var bodyDocument = JsonDocument.Parse(body);
                bodyDocument.RootElement.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
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
