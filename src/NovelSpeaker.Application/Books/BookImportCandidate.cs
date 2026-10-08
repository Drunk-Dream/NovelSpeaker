namespace NovelSpeaker.Application.Books;

public sealed record BookImportCandidate(
    string BookId,
    string Title,
    string? Author,
    DateTimeOffset ImportedAt,
    DateTimeOffset? LastPlayedAt,
    string? OriginalFileName = null);
