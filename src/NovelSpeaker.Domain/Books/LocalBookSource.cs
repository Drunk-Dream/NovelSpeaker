namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Local typed data for an imported snapshot; the stored content is persistent source data.
/// </summary>
public sealed record LocalBookSource(
    string SourceId,
    string OriginalFileName,
    string StoredContentPath,
    string SourceHash,
    string Encoding,
    DateTimeOffset ImportedAt,
    DateTimeOffset LastImportedAt);
