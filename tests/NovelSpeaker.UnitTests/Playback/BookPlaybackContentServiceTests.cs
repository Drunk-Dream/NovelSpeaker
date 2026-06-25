using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class BookPlaybackContentServiceTests
{
    [Fact]
    public async Task GetBookAsync_does_not_resume_on_the_callers_synchronization_context()
    {
        using var database = CreateDatabase();

        var service = new BookPlaybackContentService(
            new DelayedSqliteConnectionFactory(database.ConnectionString),
            new Infrastructure.Books.Parsing.TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default));

        var previousContext = SynchronizationContext.Current;
        var trackingContext = new TrackingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(trackingContext);

        Task<PlaybackBookContent?> loadTask;
        try
        {
            loadTask = service.GetBookAsync("book-1", CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        var book = await loadTask;

        Assert.NotNull(book);
        Assert.Single(book!.Chapters);
        Assert.Empty(book.Chapters[0].Segments);
        Assert.Equal(0, trackingContext.PostCount);
    }

    [Fact]
    public async Task GetChapterAsync_returns_segmented_current_chapter_only()
    {
        using var database = CreateDatabase();

        var service = new BookPlaybackContentService(
            new DelayedSqliteConnectionFactory(database.ConnectionString),
            new Infrastructure.Books.Parsing.TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default));

        var chapter = await service.GetChapterAsync("book-1", 0, CancellationToken.None);

        Assert.NotNull(chapter);
        Assert.Single(chapter!.Segments);
        Assert.Equal("第一段。", chapter.Segments[0].SpeechText);
    }

    private static TestDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.db");
        var connectionString = $"Data Source={path}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var createBooks = connection.CreateCommand();
        createBooks.CommandText =
            """
            CREATE TABLE Books (
                Id TEXT NOT NULL PRIMARY KEY,
                Title TEXT NOT NULL
            );
            """;
        createBooks.ExecuteNonQuery();

        using var createChapters = connection.CreateCommand();
        createChapters.CommandText =
            """
            CREATE TABLE Chapters (
                Id TEXT NOT NULL PRIMARY KEY,
                BookId TEXT NOT NULL,
                ChapterIndex INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                StartOffset INTEGER NOT NULL,
                Length INTEGER NOT NULL
            );
            """;
        createChapters.ExecuteNonQuery();

        using var insertBook = connection.CreateCommand();
        insertBook.CommandText = "INSERT INTO Books (Id, Title) VALUES ('book-1', '示例小说');";
        insertBook.ExecuteNonQuery();

        using var insertChapter = connection.CreateCommand();
        insertChapter.CommandText =
            """
            INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, Content, StartOffset, Length)
            VALUES ('chapter-1', 'book-1', 0, 0, '第一章', '第一段。', 0, 4);
            """;
        insertChapter.ExecuteNonQuery();

        return new TestDatabase(path, connectionString);
    }

    private sealed class DelayedSqliteConnectionFactory : ISqliteConnectionFactory
    {
        private readonly string _connectionString;

        public DelayedSqliteConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
    }

    private sealed class StaticTextSegmentationOptionsProvider : ITextSegmentationOptionsProvider
    {
        private readonly TextSegmentationOptions _options;

        public StaticTextSegmentationOptionsProvider(TextSegmentationOptions options)
        {
            _options = options;
        }

        public TextSegmentationOptions GetCurrent() => _options;
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(static callbackState =>
            {
                var (callback, callbackArgument) = ((SendOrPostCallback Callback, object? State))callbackState!;
                callback(callbackArgument);
            }, (d, state));
        }
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _path;

        public TestDatabase(string path, string connectionString)
        {
            _path = path;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
