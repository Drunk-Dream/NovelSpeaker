using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Loads persisted books for playback, separating chapter metadata from on-demand text segmentation.
/// </summary>
public interface IBookPlaybackContentService
{
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
                    .Select(static chapter => PlaybackChapterContent.Unloaded(chapter.ChapterIndex, chapter.Title))
                    .ToArray(),
                book.BookAuthor);
    }

    Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken);

    async Task<IReadOnlyList<PlaybackChapterContent>> GetChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        var chapters = new List<PlaybackChapterContent>(chapterIndices.Count);
        foreach (var chapterIndex in chapterIndices.Distinct().Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapter = await GetChapterAsync(bookId, chapterIndex, cancellationToken).ConfigureAwait(false);
            if (chapter is not null)
            {
                chapters.Add(chapter);
            }
        }

        return chapters;
    }
}
