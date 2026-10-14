using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Loads persisted books for playback, separating chapter metadata from on-demand text segmentation.
/// </summary>
public interface IBookPlaybackContentService
{
    /// <summary>Captures text settings and rules without loading any chapter body.</summary>
    Task<ChapterContentReader> FreezeChapterReaderAsync(CancellationToken cancellationToken);

    Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken);

    async Task<PlaybackBookContent?> GetBookAsync(
        string bookId,
        IReadOnlyList<BookChapterSummary> catalog,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(catalog);

        var book = await GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        return book is null
            ? null
            : new PlaybackBookContent(
                book.BookId,
                book.BookTitle,
                catalog
                    .Select(static chapter => PlaybackChapterContent.Unloaded(chapter.ChapterIndex, chapter.Title, chapter.ChapterId))
                    .ToArray(),
                book.BookAuthor,
                book.SourceContext);
    }

    async Task<bool> IsCurrentAsync(PlaybackBookContent book, CancellationToken cancellationToken)
    {
        var current = await GetBookAsync(book.BookId, cancellationToken).ConfigureAwait(false);
        return current is not null && current.SourceContext == book.SourceContext;
    }

    Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken);

}

/// <summary>Loads one chapter using the captured text-processing configuration.</summary>
public delegate Task<PlaybackChapterContent?> ChapterContentReader(
    string bookId, int chapterIndex, CancellationToken cancellationToken);
