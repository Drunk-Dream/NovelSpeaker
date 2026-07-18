using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class CacheWorkspaceServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_projects_store_summary()
    {
        var store = new FakeAudioCacheStore
        {
            SummaryResult = new AudioCacheStoreSummary(4096, 3, 2048, true)
        };
        var service = CreateService(store, new FakeBookPlaybackMetadataQuery(), new FakeBookContentReader());

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(new CacheOverviewModel(4096, 3, 2048, true), overview);
    }

    [Fact]
    public async Task GetCachedBooksAsync_enriches_titles_and_authors_through_books_query()
    {
        var store = new FakeAudioCacheStore
        {
            BooksResult = [new CachedBookStoreSummary("book-1", 2, 3, 4096)]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Books["book-1"] = new PlaybackBookMetadata("book-1", "示例书", "作者甲", []);
        var service = CreateService(store, metadata, new FakeBookContentReader());

        var books = await service.GetCachedBooksAsync(CancellationToken.None);

        var book = Assert.Single(books);
        Assert.Equal("示例书", book.Title);
        Assert.Equal("作者甲", book.Author);
        Assert.Equal(2, book.ChapterCount);
        Assert.Equal(["book-1"], metadata.RequestedBookIds);
    }

    [Fact]
    public async Task GetCachedBooksAsync_falls_back_when_book_no_longer_exists()
    {
        var store = new FakeAudioCacheStore
        {
            BooksResult = [new CachedBookStoreSummary("orphan", 1, 2, 1024)]
        };
        var service = CreateService(store, new FakeBookPlaybackMetadataQuery(), new FakeBookContentReader());

        var book = Assert.Single(await service.GetCachedBooksAsync(CancellationToken.None));

        Assert.Equal("orphan", book.Title);
        Assert.Null(book.Author);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_estimates_completeness_and_handles_expected_read_failures()
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult =
            [
                new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024),
                new CachedChapterStoreSummary("book-1", 1, 2, 2, 2048)
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(0, "第一章", "content.txt", 0, 4);
        metadata.Chapters[("book-1", 1)] = new PlaybackChapterMetadata(1, "第二章", "content.txt", 4, 4);
        var reader = new FakeBookContentReader();
        reader.TextByStartOffset[0] = "甲。\n乙。";
        reader.ExceptionByStartOffset[4] = new FileNotFoundException("missing");
        var service = CreateService(store, metadata, reader);

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
    public async Task GetCachedChaptersAsync_propagates_cancellation_from_content_read()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new FakeBookContentReader();
        reader.ExceptionByStartOffset[0] = new OperationCanceledException(cancellation.Token);
        var service = CreateChapterService(reader);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetCachedChaptersAsync("book-1", cancellation.Token));
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_propagates_unexpected_content_failures()
    {
        var reader = new FakeBookContentReader();
        reader.ExceptionByStartOffset[0] = new ApplicationException("unexpected defect");
        var service = CreateChapterService(reader);

        var exception = await Assert.ThrowsAsync<ApplicationException>(() =>
            service.GetCachedChaptersAsync("book-1", CancellationToken.None));

        Assert.Equal("unexpected defect", exception.Message);
    }

    [Fact]
    public async Task Cleanup_and_maintenance_delegate_to_store_and_preserve_result_fields()
    {
        var store = new FakeAudioCacheStore
        {
            CleanupResult = new AudioCacheStoreCleanupResult(8192, 4, 2, 1)
        };
        var service = CreateService(store, new FakeBookPlaybackMetadataQuery(), new FakeBookContentReader());

        await service.TrimToConfiguredLimitAsync(CancellationToken.None);
        var chapter = await service.ClearChapterAsync("book-1", 2, CancellationToken.None);
        var book = await service.ClearBookAsync("book-1", CancellationToken.None);
        var all = await service.ClearAllAsync(CancellationToken.None);

        Assert.True(store.MaintenanceRequested);
        Assert.Equal(("book-1", 2), store.ClearedChapter);
        Assert.Equal("book-1", store.ClearedBookId);
        Assert.True(store.ClearAllRequested);
        Assert.Equal(new CacheCleanupResult(8192, 4, 2, 1), chapter);
        Assert.Equal(chapter, book);
        Assert.Equal(chapter, all);
    }

    private static CacheWorkspaceService CreateChapterService(IBookContentReader reader)
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult = [new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024)]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(0, "第一章", "content.txt", 0, 4);
        return CreateService(store, metadata, reader);
    }

    private static CacheWorkspaceService CreateService(
        IAudioCacheStore store,
        IBookPlaybackMetadataQuery metadataQuery,
        IBookContentReader reader)
    {
        return new CacheWorkspaceService(
            store,
            metadataQuery,
            reader,
            new TextSegmenter(),
            new FixedSegmentationOptionsProvider());
    }

    private sealed class FakeAudioCacheStore : IAudioCacheStore
    {
        public AudioCacheStoreSummary SummaryResult { get; set; } =
            new(0, 0, AppSettings.DefaultCacheLimitBytes, false);

        public IReadOnlyList<CachedBookStoreSummary> BooksResult { get; set; } = [];

        public IReadOnlyList<CachedChapterStoreSummary> ChaptersResult { get; set; } = [];

        public AudioCacheStoreCleanupResult CleanupResult { get; set; } = new(0, 0, 0, 0);

        public bool MaintenanceRequested { get; private set; }

        public (string BookId, int ChapterIndex)? ClearedChapter { get; private set; }

        public string? ClearedBookId { get; private set; }

        public bool ClearAllRequested { get; private set; }

        public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(SummaryResult);

        public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) => Task.FromResult(BooksResult);

        public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult(ChaptersResult);

        public Task<AudioCacheStoreCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            ClearedChapter = (bookId, chapterIndex);
            return Task.FromResult(CleanupResult);
        }

        public Task<AudioCacheStoreCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
        {
            ClearedBookId = bookId;
            return Task.FromResult(CleanupResult);
        }

        public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
        {
            ClearAllRequested = true;
            return Task.FromResult(CleanupResult);
        }

        public Task RunMaintenanceAsync(CancellationToken cancellationToken)
        {
            MaintenanceRequested = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBookPlaybackMetadataQuery : IBookPlaybackMetadataQuery
    {
        public Dictionary<string, PlaybackBookMetadata> Books { get; } = [];

        public Dictionary<(string BookId, int ChapterIndex), PlaybackChapterMetadata> Chapters { get; } = [];

        public List<string> RequestedBookIds { get; } = [];

        public Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            RequestedBookIds.Add(bookId);
            return Task.FromResult(Books.GetValueOrDefault(bookId));
        }

        public Task<PlaybackChapterMetadata?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            return Task.FromResult(Chapters.GetValueOrDefault((bookId, chapterIndex)));
        }
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
