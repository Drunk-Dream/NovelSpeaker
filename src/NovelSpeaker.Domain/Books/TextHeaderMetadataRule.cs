namespace NovelSpeaker.Domain.Books;

public sealed record TextHeaderMetadataRule(
    string Id,
    string Name,
    string Pattern,
    int SortOrder,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
