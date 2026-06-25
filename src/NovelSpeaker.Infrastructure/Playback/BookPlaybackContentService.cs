using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Loads playback book metadata first and segments individual chapters only when playback needs them.
/// </summary>
public sealed class BookPlaybackContentService : IBookPlaybackContentService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ITextSegmenter _textSegmenter;
    private readonly ITextSegmentationOptionsProvider _optionsProvider;

    public BookPlaybackContentService(
        ISqliteConnectionFactory connectionFactory,
        ITextSegmenter textSegmenter,
        ITextSegmentationOptionsProvider optionsProvider)
    {
        _connectionFactory = connectionFactory;
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
            SELECT Id, Title
            FROM Books
            WHERE Id = $id;
            """;
        bookCommand.Parameters.AddWithValue("$id", bookId);

        string? title = null;
        await using (var bookReader = await bookCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await bookReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            title = bookReader.GetString(1);
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

        return new PlaybackBookContent(bookId, title!, chapters);
    }

    public async Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var chapterCommand = connection.CreateCommand();
        chapterCommand.CommandText =
            """
            SELECT Id, BookId, ChapterIndex, SortOrder, Title, Content, StartOffset, Length
            FROM Chapters
            WHERE BookId = $bookId AND ChapterIndex = $chapterIndex
            ORDER BY SortOrder, ChapterIndex
            LIMIT 1;
            """;
        chapterCommand.Parameters.AddWithValue("$bookId", bookId);
        chapterCommand.Parameters.AddWithValue("$chapterIndex", chapterIndex);

        await using var chapterReader = await chapterCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await chapterReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var chapter = new Chapter(
            chapterReader.GetString(0),
            chapterReader.GetString(1),
            chapterReader.GetInt32(2),
            chapterReader.GetInt32(3),
            chapterReader.GetString(4),
            chapterReader.GetString(5),
            chapterReader.GetInt32(6),
            chapterReader.GetInt32(7));
        var options = _optionsProvider.GetCurrent();

        var segments = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _textSegmenter.Segment(chapter, options);
        }, cancellationToken).ConfigureAwait(false);

        return new PlaybackChapterContent(
            chapter.ChapterIndex,
            chapter.Title,
            segments);
    }
}
