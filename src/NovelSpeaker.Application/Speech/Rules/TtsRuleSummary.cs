namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>
/// Provides the lightweight information needed by the rules list page.
/// </summary>
public sealed record TtsRuleSummary(
    long Id,
    string Name,
    bool IsEnabled,
    bool IsSelected,
    DateTimeOffset? LastUsedAt);
