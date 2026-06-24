using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Loads persisted books and computes playback-ready speech segments on demand.
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

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var bookCommand = connection.CreateCommand();
        bookCommand.CommandText =
            """
            SELECT Id, Title
            FROM Books
            WHERE Id = $id;
            """;
        bookCommand.Parameters.AddWithValue("$id", bookId);

        string? title = null;
        await using (var bookReader = await bookCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await bookReader.ReadAsync(cancellationToken))
            {
                return null;
            }

            title = bookReader.GetString(1);
        }

        var chapterCommand = connection.CreateCommand();
        chapterCommand.CommandText =
            """
            SELECT Id, BookId, ChapterIndex, SortOrder, Title, Content, StartOffset, Length
            FROM Chapters
            WHERE BookId = $bookId
            ORDER BY SortOrder, ChapterIndex;
            """;
        chapterCommand.Parameters.AddWithValue("$bookId", bookId);

        var chapters = new List<PlaybackChapterContent>();
        var options = _optionsProvider.GetCurrent();

        await using var chapterReader = await chapterCommand.ExecuteReaderAsync(cancellationToken);
        while (await chapterReader.ReadAsync(cancellationToken))
        {
            var chapter = new Chapter(
                chapterReader.GetString(0),
                chapterReader.GetString(1),
                chapterReader.GetInt32(2),
                chapterReader.GetInt32(3),
                chapterReader.GetString(4),
                chapterReader.GetString(5),
                chapterReader.GetInt32(6),
                chapterReader.GetInt32(7));

            var segments = _textSegmenter.Segment(chapter, options);
            chapters.Add(new PlaybackChapterContent(
                chapter.ChapterIndex,
                chapter.Title,
                segments));
        }

        return new PlaybackBookContent(bookId, title!, chapters);
    }
}
