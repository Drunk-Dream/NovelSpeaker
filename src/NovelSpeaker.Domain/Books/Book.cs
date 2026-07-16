namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents an imported book record that points to the stored normalized TXT file.
/// </summary>
public sealed record Book(
    string Id,
    string Title,
    string? Author,
    string OriginalFileName,
    string StoredFilePath,
    string SourceHash,
    string Encoding,
    DateTimeOffset ImportedAt,
    DateTimeOffset LastImportedAt,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset UpdatedAt);
