using System.Text.Json;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Rules;

internal static class TtsRuleModelMapper
{
    public static TtsRuleSummary ToSummary(HttpTtsRule rule, long? selectedId) =>
        new(rule.Id, rule.Name, rule.IsEnabled, selectedId == rule.Id && rule.IsEnabled, rule.LastUsedAt);

    public static TtsRuleEditorModel ToEditor(HttpTtsRule rule) => new(
        rule.Id, rule.Name, rule.IsEnabled, rule.Url, rule.ContentType, rule.ConcurrentRate, rule.LastUpdateTime,
        rule.Headers.Select(pair => new TtsRuleEditorKeyValue(pair.Key, pair.Value)).ToArray(),
        new TtsRuleRequestOptionsEditor(rule.RequestMethod, rule.RequestBody));

    public static TtsRuleEditorModel Normalize(TtsRuleEditorModel editor) => editor with
    {
        Name = editor.Name.Trim(),
        Url = editor.Url.Trim(),
        ContentType = Optional(editor.ContentType),
        ConcurrentRate = Optional(editor.ConcurrentRate),
        Headers = editor.Headers.Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => new TtsRuleEditorKeyValue(entry.Key.Trim(), entry.Value)).ToArray(),
        RequestOptions = editor.RequestOptions with
        {
            Method = Optional(editor.RequestOptions.Method)?.ToUpperInvariant(),
            Body = Optional(editor.RequestOptions.Body)
        }
    };

    public static HttpTtsRule BuildRule(TtsRuleEditorModel editor, HttpTtsRule? existing, DateTimeOffset utcNow)
    {
        var body = ParseBody(editor.RequestOptions.Body);
        return new HttpTtsRule(editor.Id ?? 0, editor.Name, editor.Url, editor.ContentType, editor.ConcurrentRate,
            editor.Headers.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            editor.RequestOptions.Method, body.Text, body.IsJson, editor.LastUpdateTime, editor.IsEnabled,
            existing?.LastUsedAt, existing?.CreatedAt ?? utcNow, utcNow);
    }

    public static HttpTtsRule EnsureUniqueName(HttpTtsRule rule, IReadOnlyList<HttpTtsRule> existing, long? currentId)
    {
        var baseName = string.IsNullOrWhiteSpace(rule.Name) ? "新建规则" : rule.Name.Trim();
        if (!HasName(existing, baseName, currentId))
        {
            return rule with { Name = baseName };
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!HasName(existing, candidate, currentId))
            {
                return rule with { Name = candidate };
            }
        }
    }

    private static bool HasName(IEnumerable<HttpTtsRule> rules, string name, long? currentId) =>
        rules.Any(rule => rule.Id != currentId && string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (string? Text, bool IsJson) ParseBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, false);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? (document.RootElement.GetString(), false)
                : (document.RootElement.GetRawText(), true);
        }
        catch (JsonException)
        {
            return (body, false);
        }
    }
}
