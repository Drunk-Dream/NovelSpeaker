using System.Text.Json;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal static class TtsRuleCompatibilityAnalyzer
{
    private static readonly HashSet<string> SupportedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "url",
        "contentType",
        "concurrentRate",
        "header",
        "enabledCookieJar",
        "isEnabled",
        "lastUpdateTime"
    };

    private static readonly HashSet<string> DeferredFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "loginUrl",
        "loginUi",
        "loginCheckJs",
        "jsLib"
    };

    public static (TtsRuleCompatibilityStatus Status, IReadOnlyList<string> UnsupportedFields) Analyze(JsonElement root)
    {
        var unsupportedFields = new List<string>();
        var needsManualAdjustment = false;

        foreach (var property in root.EnumerateObject())
        {
            if (SupportedFields.Contains(property.Name))
            {
                continue;
            }

            unsupportedFields.Add(property.Name);
            if (DeferredFields.Contains(property.Name))
            {
                needsManualAdjustment = true;
            }
        }

        var orderedFields = unsupportedFields
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (orderedFields.Length == 0)
        {
            return (TtsRuleCompatibilityStatus.Compatible, orderedFields);
        }

        return needsManualAdjustment
            ? (TtsRuleCompatibilityStatus.NeedsManualAdjustment, orderedFields)
            : (TtsRuleCompatibilityStatus.CompatibleWithWarnings, orderedFields);
    }
}
