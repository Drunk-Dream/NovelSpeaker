namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents a book's independent content source and its metadata snapshot.
/// </summary>
public sealed record BookSource(
    string Id,
    string BookId,
    SourceType Type,
    string Title,
    string? Author,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
