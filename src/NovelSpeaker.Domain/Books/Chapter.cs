namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents a technical entry in a source-owned catalog.
/// </summary>
public sealed record Chapter(
    string Id,
    string SourceId,
    int ChapterIndex,
    int SortOrder,
    string Title);
