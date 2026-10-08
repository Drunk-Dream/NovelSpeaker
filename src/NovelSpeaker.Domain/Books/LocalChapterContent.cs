namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Locates a catalog entry's content in its local source's normalized text.
/// </summary>
public sealed record LocalChapterContent(
    string ChapterId,
    int StartOffset,
    int Length);
