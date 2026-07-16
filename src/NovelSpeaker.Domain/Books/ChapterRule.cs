namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents one database-backed chapter-detection rule.
/// </summary>
public sealed record ChapterRule(
    string Id,
    string Name,
    string Pattern,
    int SortOrder,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
