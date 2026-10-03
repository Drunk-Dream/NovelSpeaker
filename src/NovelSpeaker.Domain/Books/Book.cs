namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents a stable book identity and its current display metadata snapshot.
/// </summary>
public sealed record Book(
    string Id,
    string Title,
    string? Author,
    string? ActiveSourceId,
    DateTimeOffset ImportedAt,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset UpdatedAt,
    string? Description = null);
