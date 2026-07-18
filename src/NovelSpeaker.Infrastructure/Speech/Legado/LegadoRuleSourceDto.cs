using System.Text.Json;

namespace NovelSpeaker.Infrastructure.Speech.Legado;

internal sealed record LegadoRuleSourceDto(
    string? Name,
    string? Url,
    string? Header,
    string? RequestOptionsJson,
    string? ContentType,
    string? ConcurrentRate,
    long? LastUpdateTime,
    bool IsEnabled,
    bool HasUnsupportedCookieOrLoginInfoFields,
    IReadOnlyList<string> UnsupportedFields)
{
    public static LegadoRuleSourceDto FromJson(JsonElement element, IReadOnlySet<string> supportedFields)
    {
        return new LegadoRuleSourceDto(
            ReadString(element, "name"),
            ReadString(element, "url"),
            ReadString(element, "header"),
            ReadRawJson(element, "requestOptions"),
            ReadString(element, "contentType"),
            ReadString(element, "concurrentRate"),
            ReadInt64(element, "lastUpdateTime"),
            ReadBoolean(element, "isEnabled", true),
            HasProperty(element, "enabledCookieJar") || HasProperty(element, "loginInfo"),
            element.EnumerateObject()
                .Where(property => !supportedFields.Contains(property.Name))
                .Select(property => property.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static bool HasProperty(JsonElement element, string name) =>
        element.EnumerateObject().Any(property =>
            property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static JsonElement? Find(JsonElement element, string name) =>
        element.EnumerateObject().FirstOrDefault(property =>
            string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? ReadString(JsonElement element, string name)
    {
        var value = Find(element, name);
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => value.Value.GetRawText()
        };
    }

    private static string? ReadRawJson(JsonElement element, string name)
    {
        var value = Find(element, name);
        return value is null || value.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : value.Value.GetRawText();
    }

    private static long? ReadInt64(JsonElement element, string name)
    {
        var value = Find(element, name);
        return value is { ValueKind: JsonValueKind.Number } && value.Value.TryGetInt64(out var number)
            ? number
            : value is { ValueKind: JsonValueKind.String } && long.TryParse(value.Value.GetString(), out number)
                ? number
                : null;
    }

    private static bool ReadBoolean(JsonElement element, string name, bool defaultValue)
    {
        var value = Find(element, name);
        return value?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.Value.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }
}
