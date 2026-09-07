using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class CacheCatalogTests
{
    [Fact]
    public async Task GetCachedChaptersAsync_returns_physical_summaries_without_coverage()
    {
        var store = new FakeAudioCacheStore
        {
            Chapters =
            [
                new CachedChapterStoreSummary("book-1", 3, 7, 9, 900),
                new CachedChapterStoreSummary("book-1", 1, 2, 4, 400)
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 1)] = new PlaybackChapterMetadata(1, "第一章", "", 0, 0);
        metadata.Chapters[("book-1", 3)] = new PlaybackChapterMetadata(3, "第三章", "", 0, 0);
        var catalog = new CacheCatalog(store, metadata);

        var chapters = await catalog.GetCachedChaptersAsync("book-1", [3, 1], CancellationToken.None);

        Assert.Equal([1, 3], chapters.Select(chapter => chapter.ChapterIndex));
        Assert.Equal("第一章", chapters[0].Title);
        Assert.Equal(2, chapters[0].DistinctSegmentCount);
        Assert.Equal(4, chapters[0].EntryCount);
        Assert.Equal(400, chapters[0].TotalSizeBytes);
    }

    private sealed class FakeAudioCacheStore : IAudioCacheStore
    {
        public event EventHandler<CacheChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<CachedChapterStoreSummary> Chapters { get; init; } = [];

        public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AudioCacheStoreSummary(0, 0, 1, false));

        public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CachedBookStoreSummary>>([]);

        public Task<CachedBookStoreSummary?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            Task.FromResult<CachedBookStoreSummary?>(null);

        public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Chapters);

        public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CachedChapterStoreSummary>>(
                Chapters.Where(chapter => chapterIndices.Contains(chapter.ChapterIndex)).ToArray());

        public Task<CachedChapterStoreSummary?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult(Chapters.FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex));

        public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
            IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
            SynthesisProfileFingerprint synthesisProfile,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChapterCacheStatus>>([]);

        public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
            IReadOnlyCollection<AudioCacheKey> keys,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<AudioCacheKey>>(new HashSet<AudioCacheKey>());

        public Task<AudioCacheStoreCleanupResult> ClearChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AudioCacheStoreCleanupResult> ClearBookAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RunMaintenanceAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeBookPlaybackMetadataQuery : IBookPlaybackMetadataQuery
    {
        public Dictionary<(string BookId, int ChapterIndex), PlaybackChapterMetadata> Chapters { get; } = [];

        public Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackBookMetadata?>(null);

        public Task<PlaybackChapterMetadata?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult(Chapters.GetValueOrDefault((bookId, chapterIndex)));
    }
}
