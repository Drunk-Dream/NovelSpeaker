using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>A complete prepared snapshot, with the previous path used to reject stale updates.</summary>
public sealed record LocalSourceImportSnapshot(
    Book Book,
    BookSource Source,
    LocalBookSource LocalSource,
    IReadOnlyList<Chapter> Catalog,
    IReadOnlyList<LocalChapterContent> Contents,
    bool IsNewBook,
    string? ExpectedContentPath);
