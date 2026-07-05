namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Provides the lightweight information needed by the rules list page.
/// </summary>
public sealed record TtsRuleSummary(
    long Id,
    string Name,
    bool IsEnabled,
    bool IsSelected,
    string? LastUsedAt);
