using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.Books.Parsing;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class CacheWorkspaceServiceTests
{
    [Fact]
    public async Task GetCachedBooksAsync_enriches_titles_and_authors()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", "示例书", "作者甲");
        var service = new CacheWorkspaceService(
            new FakeAudioCacheManagementService
            {
                BooksResult = [new CachedBookSummary("book-1", 2, 3, 4096)]
            },
            fixture.Factory,
            new FakeBookContentReader(),
            new TextSegmenter(),
            new FixedSegmentationOptionsProvider());

        var books = await service.GetCachedBooksAsync(CancellationToken.None);

        var book = Assert.Single(books);
        Assert.Equal("示例书", book.Title);
        Assert.Equal("作者甲", book.Author);
        Assert.Equal(2, book.ChapterCount);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_estimates_completeness_and_handles_read_failures()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", "示例书", "作者甲");
        var reader = new FakeBookContentReader();
        reader.TextByStartOffset[0] = "甲。\n乙。";
        reader.ExceptionByStartOffset[4] = new FileNotFoundException("missing");
        var service = new CacheWorkspaceService(
            new FakeAudioCacheManagementService
            {
                ChaptersResult =
                [
                    new CachedChapterSummary("book-1", 0, 1, 1, 1024),
                    new CachedChapterSummary("book-1", 1, 2, 2, 2048)
                ]
            },
            fixture.Factory,
            reader,
            new TextSegmenter(),
            new FixedSegmentationOptionsProvider());

        var chapters = await service.GetCachedChaptersAsync("book-1", CancellationToken.None);

        Assert.Equal(2, chapters.Count);
        var first = Assert.Single(chapters, item => item.ChapterIndex == 0);
        Assert.Equal("第一章", first.Title);
        Assert.Equal(2, first.EstimatedTotalSegmentCount);
        var second = Assert.Single(chapters, item => item.ChapterIndex == 1);
        Assert.Null(second.EstimatedTotalSegmentCount);
        Assert.Equal(2, second.CachedSegmentCount);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_propagates_cancellation_from_segment_estimation()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", "示例书", "作者甲");
        using var cancellation = new CancellationTokenSource();
        var reader = new FakeBookContentReader();
        reader.ExceptionByStartOffset[0] = new OperationCanceledException(cancellation.Token);
        var service = CreateChapterService(fixture, reader);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetCachedChaptersAsync("book-1", cancellation.Token));
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_propagates_unexpected_estimation_failures()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", "示例书", "作者甲");
        var reader = new FakeBookContentReader();
        reader.ExceptionByStartOffset[0] = new ApplicationException("unexpected defect");
        var service = CreateChapterService(fixture, reader);

        var exception = await Assert.ThrowsAsync<ApplicationException>(() =>
            service.GetCachedChaptersAsync("book-1", CancellationToken.None));

        Assert.Equal("unexpected defect", exception.Message);
    }

    private static CacheWorkspaceService CreateChapterService(TestFixture fixture, IBookContentReader reader)
    {
        return new CacheWorkspaceService(
            new FakeAudioCacheManagementService
            {
                ChaptersResult = [new CachedChapterSummary("book-1", 0, 1, 1, 1024)]
            },
            fixture.Factory,
            reader,
            new TextSegmenter(),
            new FixedSegmentationOptionsProvider());
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);
        return new TestFixture(directories, factory);
    }

    private static async Task SeedBookAsync(TestFixture fixture, string bookId, string title, string? author)
    {
        var storedDirectory = Path.Combine(fixture.Directories.BooksDirectoryPath, bookId);
        Directory.CreateDirectory(storedDirectory);
        var storedFilePath = Path.Combine(storedDirectory, "content.txt");
        await File.WriteAllTextAsync(storedFilePath, "第一章内容第二章内容", CancellationToken.None);

        var repository = new BookImportRepository(fixture.Factory);
        var now = DateTimeOffset.UtcNow;
        await repository.SaveAsync(
            new Domain.Books.Book(
                bookId,
                title,
                author,
                $"{bookId}.txt",
                storedFilePath,
                $"{bookId}-hash",
                "utf-8",
                now,
                now,
                null,
                now),
            [
                new Domain.Books.Chapter($"{bookId}-1", bookId, 0, 0, "第一章", 0, 4),
                new Domain.Books.Chapter($"{bookId}-2", bookId, 1, 1, "第二章", 4, 4)
            ],
            CancellationToken.None);
    }

    private sealed record TestFixture(LocalAppDataDirectoryProvider Directories, SqliteConnectionFactory Factory);

    private sealed class FakeAudioCacheManagementService : IAudioCacheManagementService
    {
        public IReadOnlyList<CachedBookSummary> BooksResult { get; set; } = [];
        public IReadOnlyList<CachedChapterSummary> ChaptersResult { get; set; } = [];

        public Task<AudioCacheSummary> GetSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(new AudioCacheSummary(0, 0, AppSettings.DefaultCacheLimitBytes, false));
        public Task<IReadOnlyList<CachedBookSummary>> GetBooksAsync(CancellationToken cancellationToken) => Task.FromResult(BooksResult);
        public Task<IReadOnlyList<CachedChapterSummary>> GetChaptersAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult(ChaptersResult);
        public Task<AudioCacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AudioCacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AudioCacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RunMaintenanceAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeBookContentReader : IBookContentReader
    {
        public int CallCount { get; private set; }

        public Dictionary<int, string> TextByStartOffset { get; } = [];

        public Dictionary<int, Exception> ExceptionByStartOffset { get; } = [];

        public Task<string> ReadChapterTextAsync(string storedFilePath, int startOffset, int length, CancellationToken cancellationToken)
        {
            CallCount++;
            if (ExceptionByStartOffset.TryGetValue(startOffset, out var exception))
            {
                throw exception;
            }

            return Task.FromResult(TextByStartOffset[startOffset]);
        }
    }

    private sealed class FixedSegmentationOptionsProvider : ITextSegmentationOptionsProvider
    {
        public TextSegmentationOptions GetCurrent() => TextSegmentationOptions.Default;
    }
}
