using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;
using NovelSpeaker.TestKit.Common;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class PlaybackContentResolverTests
{
    [Fact]
    public async Task Source_replacement_during_processing_rejects_late_chapter_result()
    {
        var metadata = new FixedMetadataQuery { Context = new("source", "old") };
        var pipeline = new DelayedRegexReplacementPipeline();
        var service = new PlaybackContentResolver(metadata, new FixedSourceContentReader("正文"),
            new TextSegmenter(), new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default), pipeline);
        var load = service.GetChapterAsync("book-1", 0, CancellationToken.None);
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        metadata.Context = new("source", "new");
        pipeline.Complete();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
    }

    [Fact]
    public async Task GetChapterAsync_reads_from_content_file_and_segments_relative_to_chapter_text()
    {
        using var database = CreateDatabase(createContentFile: true);

        var service = new PlaybackContentResolver(
            new SqliteBookPlaybackMetadataQuery(new TestSqliteConnectionFactory(database.ConnectionString)),
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
    public async Task GetChapterAsync_prepends_title_when_reading_titles_is_enabled()
    {
        var service = new PlaybackContentResolver(
            new FixedMetadataQuery(),
            new FixedSourceContentReader("第一段。"),
            new TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default),
            new PassthroughRegexReplacementPipeline(),
            new StaticAppSettingsService(AppSettings.Default with { ReadChapterTitle = true }));

        var chapter = await service.GetChapterAsync("book-1", 0, CancellationToken.None);

        Assert.NotNull(chapter);
        Assert.Equal(2, chapter!.Segments.Count);
        Assert.Equal("第一章", chapter.Segments[0].SpeechText);
        Assert.True(chapter.Segments[0].IsChapterTitle);
        Assert.Equal(0, chapter.Segments[0].SegmentIndex);
        Assert.Equal(0, chapter.Segments[0].StartOffset);
        Assert.Equal("第一段。", chapter.Segments[1].SpeechText);
        Assert.False(chapter.Segments[1].IsChapterTitle);
        Assert.Equal(1, chapter.Segments[1].SegmentIndex);
        Assert.Equal(0, chapter.Segments[1].StartOffset);
    }

    [Fact]
    public async Task GetChaptersAsync_loads_requested_chapters_in_catalog_order()
    {
        using var database = CreateDatabase(createContentFile: true);
        await using (var connection = new SqliteConnection(database.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Chapters (Id, BookId, SourceBindingId, ChapterIndex, SortOrder, Title) VALUES
                ('chapter-2', 'book-1', 'local:' || 'book-1', 1, 1, '第二章');
                INSERT INTO LocalChapterContents (ChapterId, StartOffset, Length) VALUES
                ('chapter-2', 3, 9);
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var connectionFactory = new TestSqliteConnectionFactory(database.ConnectionString);
        var service = new PlaybackContentResolver(
            new SqliteBookPlaybackMetadataQuery(connectionFactory),
            CreateContentReader(database),
            new TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default),
            new PassthroughRegexReplacementPipeline());

        var chapters = await service.GetChaptersAsync("book-1", [1, 0], CancellationToken.None);

        Assert.Equal([0, 1], chapters.Select(chapter => chapter.ChapterIndex));
        Assert.All(chapters, chapter => Assert.Equal(PlaybackChapterLoadState.Loaded, chapter.LoadState));
    }

    [Fact]
    public async Task GetChapterAsync_marks_regex_filtered_chapter_as_loaded_empty()
    {
        var service = new PlaybackContentResolver(
            new FixedMetadataQuery(),
            new FixedSourceContentReader("整章正文"),
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
        var service = new PlaybackContentResolver(
            new FixedMetadataQuery(),
            new FixedSourceContentReader("整章正文"),
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

    [Fact]
    public async Task GetChapterAsync_reports_expected_content_failures_without_logging_source_details()
    {
        const string privatePath = @"C:\private\novel-content.txt";
        var logger = new CapturingLogger<BookPlaybackContentFailureReporter>();
        var service = new PlaybackContentResolver(
            new FixedMetadataQuery(),
            new ThrowingSourceContentReader(new FileNotFoundException(privatePath)),
            new TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default),
            new PassthroughRegexReplacementPipeline(),
            failureReporter: new BookPlaybackContentFailureReporter(logger));

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.GetChapterAsync("book-1", 0, CancellationToken.None));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(privatePath, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("novel-content", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetChapterAsync_keeps_cancellation_normal_and_does_not_report_it()
    {
        var logger = new CapturingLogger<BookPlaybackContentFailureReporter>();
        var service = new PlaybackContentResolver(
            new FixedMetadataQuery(),
            new ThrowingSourceContentReader(new OperationCanceledException()),
            new TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default),
            new PassthroughRegexReplacementPipeline(),
            failureReporter: new BookPlaybackContentFailureReporter(logger));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetChapterAsync("book-1", 0, CancellationToken.None));

        Assert.Empty(logger.Entries);
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

        new SqliteMigrationRunner(new TestSqliteConnectionFactory(connectionString))
            .InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        using var insertBook = connection.CreateCommand();
        insertBook.CommandText = """
            INSERT INTO Books (Id, Title, Author, NormalizedTitle, NormalizedAuthor, ImportedAt, UpdatedAt)
            VALUES ('book-1', '示例小说', NULL, '示例小说', '', '2026-01-01', '2026-01-01');
            INSERT INTO BookSourceBindings (Id, BookId, SourceType, CreatedAt, UpdatedAt) VALUES ('local:book-1', 'book-1', 1, '2026-01-01', '2026-01-01');
            INSERT INTO LocalBookSourceBindings (BindingId, OriginalFileName, StoredContentPath, SourceHash, Encoding, ImportedAt, LastImportedAt)
            VALUES ('local:book-1', 'fixture.txt', $storedFilePath, 'hash', 'utf-8', '2026-01-01', '2026-01-01');
            UPDATE Books SET ActiveSourceBindingId = 'local:book-1' WHERE Id = 'book-1';
            """;
        insertBook.Parameters.AddWithValue("$storedFilePath", createContentFile ? contentPath : Path.Combine(directory, "missing-content.txt"));
        insertBook.ExecuteNonQuery();

        using var insertChapter = connection.CreateCommand();
        insertChapter.CommandText =
            """
            INSERT INTO Chapters (Id, BookId, SourceBindingId, ChapterIndex, SortOrder, Title) VALUES
            ('chapter-1', 'book-1', 'local:' || 'book-1', 0, 0, '第一章');
            INSERT INTO LocalChapterContents (ChapterId, StartOffset, Length) VALUES
            ('chapter-1', 3, 9);
            """;
        insertChapter.ExecuteNonQuery();

        return new TestDatabase(directory, connectionString);
    }

    private static SourceContentReader CreateContentReader(TestDatabase database)
    {
        var directories = new AppDataDirectoryProvider(database.DirectoryPath);
        return new SourceContentReader(new AppStoragePathResolver(directories), new TestSqliteConnectionFactory(database.ConnectionString));
    }

    private sealed class TestSqliteConnectionFactory : ISqliteConnectionFactory
    {
        private readonly string _connectionString;
        private readonly TaskCompletionSource? _openStarted;
        private readonly TaskCompletionSource? _releaseGate;
        private int _openCount;

        public TestSqliteConnectionFactory(string connectionString, bool gateOpen = false)
        {
            _connectionString = connectionString;
            if (gateOpen)
            {
                _openStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public Task OpenStarted => _openStarted?.Task ?? Task.CompletedTask;

        public int OpenCount => Volatile.Read(ref _openCount);

        public void Release() => _releaseGate?.TrySetResult();

        public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _openCount);
            _openStarted?.TrySetResult();
            if (_releaseGate is not null)
            {
                await _releaseGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

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

    private sealed class StaticAppSettingsService(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Current { get; } = settings.Normalize();

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            Task.FromResult(Current);
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
        public ActiveSourceContext? Context { get; set; }
        private static readonly PlaybackChapterMetadata Chapter =
            new("book-1", 0, "第一章", "local:book-1", "chapter-1");

        public Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult<PlaybackBookMetadata?>(
                new PlaybackBookMetadata(
                    bookId,
                    "示例小说",
                    null,
                    [new PlaybackChapterSummaryMetadata(Chapter.ChapterIndex, Chapter.Title)], Context));
        }

        public Task<PlaybackChapterMetadata?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<PlaybackChapterMetadata?>(Chapter with { SourceContext = Context });
        }
    }

    private sealed class FixedSourceContentReader : ISourceContentReader
    {
        private readonly string _content;

        public FixedSourceContentReader(string content)
        {
            _content = content;
        }

        public Task<string> ReadBookTextAsync(string bookId, ActiveSourceContext expectedContext, CancellationToken cancellationToken) => Task.FromResult(_content);

        public Task<string> ReadChapterTextAsync(
            PlaybackChapterMetadata chapter,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_content);
        }
    }

    private sealed class ThrowingSourceContentReader(Exception exception) : ISourceContentReader
    {
        public Task<string> ReadBookTextAsync(string bookId, ActiveSourceContext expectedContext, CancellationToken cancellationToken) => Task.FromException<string>(exception);

        public Task<string> ReadChapterTextAsync(
            PlaybackChapterMetadata chapter,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<string>(exception);
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
