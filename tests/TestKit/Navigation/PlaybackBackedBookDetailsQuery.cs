using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.TestKit.Navigation;

/// <summary>
/// Supplies the stable Books read model to presentation tests that already use a playback content fake.
/// </summary>
public sealed class PlaybackBackedBookDetailsQuery : IBookDetailsQuery
{
    private readonly IBookPlaybackContentService _contentService;

    public PlaybackBackedBookDetailsQuery(IBookPlaybackContentService contentService)
    {
        _contentService = contentService ?? throw new ArgumentNullException(nameof(contentService));
    }

    public async Task<BookDetailsHeader?> GetHeaderAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        var book = await _contentService.GetBookAsync(bookId, cancellationToken);
        return book is null ? null : new BookDetailsHeader(book.BookId, book.BookTitle, book.BookAuthor);
    }

    public async Task<IReadOnlyList<BookChapterSummary>> GetCatalogAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        var book = await _contentService.GetBookAsync(bookId, cancellationToken);
        return book?.Chapters
            .Select(static chapter => new BookChapterSummary(chapter.ChapterIndex, chapter.Title, 0, 0))
            .ToArray() ?? [];
    }

    public Task<BookReadingPosition?> GetReadingPositionAsync(
        string bookId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BookDetailsStatistics?> GetStatisticsAsync(
        string bookId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
