namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Provides the lightweight information needed by the rules list page.
/// </summary>
public sealed record TtsRuleSummary(
    long Id,
    string Name,
    bool IsEnabled,
    bool IsSelected,
    string? LastUsedAt,
    TtsRuleCompatibilityStatus CompatibilityStatus,
    IReadOnlyList<string> UnsupportedFields)
{
    public string UnsupportedFieldsSummary => UnsupportedFields.Count == 0
        ? "无"
        : string.Join("、", UnsupportedFields);
}
