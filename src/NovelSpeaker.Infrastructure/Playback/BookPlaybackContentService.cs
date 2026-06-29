using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Loads playback book metadata first and segments individual chapters only when playback needs them.
/// </summary>
public sealed class BookPlaybackContentService : IBookPlaybackContentService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IBookContentReader _bookContentReader;
    private readonly ITextSegmenter _textSegmenter;
    private readonly ITextSegmentationOptionsProvider _optionsProvider;

    public BookPlaybackContentService(
        ISqliteConnectionFactory connectionFactory,
        IBookContentReader bookContentReader,
        ITextSegmenter textSegmenter,
        ITextSegmentationOptionsProvider optionsProvider)
    {
        _connectionFactory = connectionFactory;
        _bookContentReader = bookContentReader;
        _textSegmenter = textSegmenter;
        _optionsProvider = optionsProvider;
    }

    public async Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var bookCommand = connection.CreateCommand();
        bookCommand.CommandText =
            """
            SELECT Id, Title, Author
            FROM Books
            WHERE Id = $id;
            """;
        bookCommand.Parameters.AddWithValue("$id", bookId);

        string? title = null;
        string? author = null;
        await using (var bookReader = await bookCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await bookReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            title = bookReader.GetString(1);
            author = bookReader.IsDBNull(2) ? null : bookReader.GetString(2);
        }

        var chapterCommand = connection.CreateCommand();
        chapterCommand.CommandText =
            """
            SELECT ChapterIndex, Title
            FROM Chapters
            WHERE BookId = $bookId
            ORDER BY SortOrder, ChapterIndex;
            """;
        chapterCommand.Parameters.AddWithValue("$bookId", bookId);

        var chapters = new List<PlaybackChapterContent>();
        await using var chapterReader = await chapterCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await chapterReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            chapters.Add(new PlaybackChapterContent(
                chapterReader.GetInt32(0),
                chapterReader.GetString(1),
                []));
        }

        return new PlaybackBookContent(bookId, title!, chapters, author);
    }

    public async Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var chapterCommand = connection.CreateCommand();
        chapterCommand.CommandText =
            """
            SELECT c.ChapterIndex, c.Title, b.StoredFilePath, c.StartOffset, c.Length
            FROM Chapters c
            INNER JOIN Books b
                ON b.Id = c.BookId
            WHERE c.BookId = $bookId AND c.ChapterIndex = $chapterIndex
            ORDER BY c.SortOrder, c.ChapterIndex
            LIMIT 1;
            """;
        chapterCommand.Parameters.AddWithValue("$bookId", bookId);
        chapterCommand.Parameters.AddWithValue("$chapterIndex", chapterIndex);

        await using var chapterReader = await chapterCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await chapterReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var resolvedChapterIndex = chapterReader.GetInt32(0);
        var title = chapterReader.GetString(1);
        var storedFilePath = chapterReader.GetString(2);
        var startOffset = chapterReader.GetInt32(3);
        var length = chapterReader.GetInt32(4);
        var chapterText = await _bookContentReader.ReadChapterTextAsync(
            storedFilePath,
            startOffset,
            length,
            cancellationToken).ConfigureAwait(false);
        var options = _optionsProvider.GetCurrent();

        var segments = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _textSegmenter.Segment(chapterText, options);
        }, cancellationToken).ConfigureAwait(false);

        return new PlaybackChapterContent(
            resolvedChapterIndex,
            title,
            segments);
    }
}
