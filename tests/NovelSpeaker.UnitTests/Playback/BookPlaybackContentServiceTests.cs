using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class BookPlaybackContentServiceTests
{
    [Fact]
    public async Task GetBookAsync_does_not_resume_on_the_callers_synchronization_context()
    {
        using var database = CreateDatabase(createContentFile: false);

        var service = new BookPlaybackContentService(
            new SqliteBookPlaybackMetadataQuery(new DelayedSqliteConnectionFactory(database.ConnectionString)),
            CreateContentReader(database),
            new TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default),
            new PassthroughRegexReplacementPipeline());

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
        Assert.Equal(PlaybackChapterLoadState.Unloaded, book.Chapters[0].LoadState);
        Assert.Equal(0, trackingContext.PostCount);
    }

    [Fact]
    public async Task GetChapterAsync_reads_from_content_file_and_segments_relative_to_chapter_text()
    {
        using var database = CreateDatabase(createContentFile: true);

        var service = new BookPlaybackContentService(
            new SqliteBookPlaybackMetadataQuery(new DelayedSqliteConnectionFactory(database.ConnectionString)),
            CreateContentReader(database),
            new TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default),
            new PassthroughRegexReplacementPipeline());

        var chapter = await service.GetChapterAsync("book-1", 0, CancellationToken.None);

        Assert.NotNull(chapter);
        Assert.Equal("第一章", chapter!.Title);
        Assert.Equal(PlaybackChapterLoadState.Loaded, chapter.LoadState);
        Assert.Equal(2, chapter.Segments.Count);
        Assert.Equal("第一段。", chapter.Segments[0].SpeechText);
        Assert.Equal(0, chapter.Segments[0].StartOffset);
        Assert.Equal("第二段。", chapter.Segments[1].SpeechText);
        Assert.Equal(5, chapter.Segments[1].StartOffset);
    }

    [Fact]
    public async Task GetChapterAsync_marks_regex_filtered_chapter_as_loaded_empty()
    {
        var service = new BookPlaybackContentService(
            new FixedMetadataQuery(),
            new FixedBookContentReader("整章正文"),
            new TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default),
            new EmptyRegexReplacementPipeline());

        var chapter = await service.GetChapterAsync("book-1", 0, CancellationToken.None);

        Assert.NotNull(chapter);
        Assert.Equal(PlaybackChapterLoadState.LoadedEmpty, chapter!.LoadState);
        Assert.Empty(chapter.Segments);
    }

    [Fact]
    public async Task GetChapterAsync_rejects_result_completed_after_cancellation()
    {
        var pipeline = new DelayedRegexReplacementPipeline();
        var service = new BookPlaybackContentService(
            new FixedMetadataQuery(),
            new FixedBookContentReader("整章正文"),
            new TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default),
            pipeline);
        using var cancellation = new CancellationTokenSource();

        var loadTask = service.GetChapterAsync("book-1", 0, cancellation.Token);
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        pipeline.Complete();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loadTask);
    }

    private static TestDatabase CreateDatabase(bool createContentFile)
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "playback.db");
        var contentPath = Path.Combine(directory, "content.txt");

        if (createContentFile)
        {
            File.WriteAllText(contentPath, "前言。第一段。\n第二段。");
        }

        var connectionString = $"Data Source={databasePath}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var createBooks = connection.CreateCommand();
        createBooks.CommandText =
            """
            CREATE TABLE Books (
                Id TEXT NOT NULL PRIMARY KEY,
                Title TEXT NOT NULL,
                Author TEXT NULL,
                StoredFilePath TEXT NOT NULL
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
                StartOffset INTEGER NOT NULL,
                Length INTEGER NOT NULL
            );
            """;
        createChapters.ExecuteNonQuery();

        using var insertBook = connection.CreateCommand();
        insertBook.CommandText = "INSERT INTO Books (Id, Title, Author, StoredFilePath) VALUES ('book-1', '示例小说', NULL, $storedFilePath);";
        insertBook.Parameters.AddWithValue("$storedFilePath", createContentFile ? contentPath : Path.Combine(directory, "missing-content.txt"));
        insertBook.ExecuteNonQuery();

        using var insertChapter = connection.CreateCommand();
        insertChapter.CommandText =
            """
            INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, StartOffset, Length)
            VALUES ('chapter-1', 'book-1', 0, 0, '第一章', 3, 9);
            """;
        insertChapter.ExecuteNonQuery();

        return new TestDatabase(directory, connectionString);
    }

    private static BookContentReader CreateContentReader(TestDatabase database)
    {
        var directories = new LocalAppDataDirectoryProvider(database.DirectoryPath);
        return new BookContentReader(new AppStoragePathResolver(directories));
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

    private sealed class PassthroughRegexReplacementPipeline : IRegexReplacementPipeline
    {
        public Task<RegexReplacementPipelineResult> ApplyAsync(
            IReadOnlyList<SpeechSegment> sourceSegments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new RegexReplacementPipelineResult(
                sourceSegments,
                new Dictionary<Guid, string>()));
        }
    }

    private sealed class EmptyRegexReplacementPipeline : IRegexReplacementPipeline
    {
        public Task<RegexReplacementPipelineResult> ApplyAsync(
            IReadOnlyList<SpeechSegment> sourceSegments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new RegexReplacementPipelineResult(
                [],
                new Dictionary<Guid, string>()));
        }
    }

    private sealed class DelayedRegexReplacementPipeline : IRegexReplacementPipeline
    {
        private readonly TaskCompletionSource<RegexReplacementPipelineResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RegexReplacementPipelineResult> ApplyAsync(
            IReadOnlyList<SpeechSegment> sourceSegments,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Complete()
        {
            _completion.TrySetResult(new RegexReplacementPipelineResult(
                [new SpeechSegment(0, 0, 4, "迟到结果", "迟到结果")],
                new Dictionary<Guid, string>()));
        }
    }

    private sealed class FixedMetadataQuery : IBookPlaybackMetadataQuery
    {
        private static readonly PlaybackChapterMetadata Chapter =
            new(0, "第一章", "books/book-1/content.txt", 0, 4);

        public Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult<PlaybackBookMetadata?>(
                new PlaybackBookMetadata(
                    bookId,
                    "示例小说",
                    null,
                    [new PlaybackChapterSummaryMetadata(Chapter.ChapterIndex, Chapter.Title)]));
        }

        public Task<PlaybackChapterMetadata?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<PlaybackChapterMetadata?>(Chapter);
        }
    }

    private sealed class FixedBookContentReader : IBookContentReader
    {
        private readonly string _content;

        public FixedBookContentReader(string content)
        {
            _content = content;
        }

        public Task<string> ReadChapterTextAsync(
            string storedFilePath,
            int startOffset,
            int length,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_content);
        }
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
        private readonly string _directory;

        public TestDatabase(string directory, string connectionString)
        {
            _directory = directory;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public string DirectoryPath => _directory;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
