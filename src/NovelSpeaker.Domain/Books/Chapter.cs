namespace NovelSpeaker.Domain.Books;

/// <summary>
/// A technical entry in a book's CurrentCatalog. SourceBindingId records its producer,
/// not ownership of a permanently retained catalog for that binding.
/// </summary>
public sealed record Chapter(
    string Id,
    string BookId,
    string SourceBindingId,
    int ChapterIndex,
    int SortOrder,
    string Title);
