namespace NovelSpeaker.Domain.Books;

/// <summary>One global, ordered runtime text replacement rule.</summary>
public sealed record RegexReplacementRule(
    Guid Id,
    string Name,
    bool IsEnabled,
    int SortOrder,
    string Pattern,
    string Replacement,
    RegexReplacementScope Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
