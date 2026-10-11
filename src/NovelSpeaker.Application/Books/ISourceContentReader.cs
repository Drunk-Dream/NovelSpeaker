namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads a book's current content without exposing paths or typed storage to consumers.
/// Implementations must reject entries or expected contexts that no longer belong to
/// the book's committed active binding and CurrentCatalog snapshot.
/// </summary>
public interface ISourceContentReader
{
    Task<string> ReadChapterTextAsync(
        PlaybackChapterMetadata chapter,
        CancellationToken cancellationToken);

    Task<string> ReadBookTextAsync(
        string bookId,
        ActiveSourceContext expectedContext,
        CancellationToken cancellationToken);
}
