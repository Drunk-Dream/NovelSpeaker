namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents an imported book record that points to the stored original TXT file.
/// </summary>
public sealed record Book(
    string Id,
    string Title,
    string? Author,
    string OriginalFileName,
    string StoredFilePath,
    string SourceHash,
    string Encoding,
    string ImportedAt,
    string LastImportedAt,
    string? LastPlayedAt,
    string UpdatedAt);
