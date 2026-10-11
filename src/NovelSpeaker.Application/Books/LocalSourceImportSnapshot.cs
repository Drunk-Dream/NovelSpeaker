using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// A complete local binding snapshot, with the previous path used to reject stale updates.
/// CurrentCatalog is supplied only when this binding becomes or remains active;
/// inactive binding imports must not persist their parsed catalog or chapter ranges.
/// </summary>
public sealed record LocalSourceImportSnapshot(
    Book Book,
    BookSourceBinding Binding,
    LocalBookSourceBinding LocalBinding,
    CurrentCatalog? CurrentCatalog,
    IReadOnlyList<LocalChapterContent> Contents,
    bool IsNewBook,
    string? ExpectedContentPath);
