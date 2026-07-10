using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>List projection for the regex replacement workspace.</summary>
public sealed record RegexReplacementRuleListItem(
    Guid Id,
    string Name,
    string PatternSummary,
    bool IsEnabled,
    int SortOrder,
    RegexReplacementScope Scope,
    string? ErrorMessage = null);
