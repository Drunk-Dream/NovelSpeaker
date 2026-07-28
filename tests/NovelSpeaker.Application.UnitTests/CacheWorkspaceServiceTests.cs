using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class CacheWorkspaceServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_projects_store_summary()
    {
        var store = new FakeAudioCacheStore
        {
            SummaryResult = new AudioCacheStoreSummary(4096, 3, 2048, true)
        };
        var service = CreateService(store, new FakeBookPlaybackMetadataQuery());

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
        var service = CreateService(store, metadata);

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
        var service = CreateService(store, new FakeBookPlaybackMetadataQuery());

        var book = Assert.Single(await service.GetCachedBooksAsync(CancellationToken.None));

        Assert.Equal("orphan", book.Title);
        Assert.Null(book.Author);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_counts_only_valid_entries_for_the_current_playback_configuration()
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult =
            [
                new CachedChapterStoreSummary("book-1", 0, 9, 9, 4096)
            ],
            ValidKeys = new HashSet<AudioCacheKey>
            {
                TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "当前文本甲"),
                TestAudioCacheKey.Create("book-1", 0, 1, 6, 12, "当前文本乙"),
                TestAudioCacheKey.Create("book-1", 0, 1, 7, 11, "当前文本乙"),
                TestAudioCacheKey.Create("book-1", 0, 1, 7, 12, "旧文本")
            }
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(0, "第一章", "content.txt", 0, 4);
        var content = new FakeBookPlaybackContentService();
        content.Chapters[("book-1", 0)] = PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [
                new SpeechSegment(0, 0, 1, "显示甲", "当前文本甲"),
                new SpeechSegment(1, 1, 1, "显示乙", "当前文本乙")
            ]);
        var service = CreateService(store, metadata, content, ruleId: 7, defaultSpeakSpeed: 12);

        var chapters = await service.GetCachedChaptersAsync("book-1", CancellationToken.None);

        var chapter = Assert.Single(chapters);
        Assert.Equal("第一章", chapter.Title);
        Assert.Equal(1, chapter.CachedSegmentCount);
        Assert.Equal(2, chapter.CurrentConfigurationSegmentCount);
        Assert.Equal(
            [
                TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "当前文本甲"),
                TestAudioCacheKey.Create("book-1", 0, 1, 7, 12, "当前文本乙")
            ],
            store.LastValidityQuery);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_batches_content_and_validity_queries_for_all_cached_chapters()
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult =
            [
                new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024),
                new CachedChapterStoreSummary("book-1", 1, 1, 1, 1024)
            ]
        };
        var content = new FakeBookPlaybackContentService();
        content.Chapters[("book-1", 0)] = PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [new SpeechSegment(0, 0, 1, "甲", "甲")]);
        content.Chapters[("book-1", 1)] = PlaybackChapterContent.FromLoaded(
            1,
            "第二章",
            [new SpeechSegment(0, 0, 1, "乙", "乙")]);
        var service = CreateService(store, new FakeBookPlaybackMetadataQuery(), content);

        var chapters = await service.GetCachedChaptersAsync("book-1", CancellationToken.None);

        Assert.Equal(2, chapters.Count);
        Assert.Equal(1, content.BatchRequestCount);
        Assert.Equal([0, 1], content.LastBatchIndices);
        Assert.Equal(1, store.ValidityQueryCount);
        Assert.Equal(2, store.LastValidityQuery.Count);
    }

    [Fact]
    public async Task GetChapterCacheStatusesAsync_returns_all_requested_chapters_in_order()
    {
        var firstKey = TestAudioCacheKey.Create("book-1", 0, 0, 7, 10, "甲");
        var store = new FakeAudioCacheStore
        {
            ValidKeys = new HashSet<AudioCacheKey> { firstKey }
        };
        var content = new FakeBookPlaybackContentService();
        content.Chapters[("book-1", 0)] = PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [new SpeechSegment(0, 0, 1, "甲", "甲")]);
        content.Chapters[("book-1", 2)] = PlaybackChapterContent.FromLoaded(
            2,
            "第三章",
            [
                new SpeechSegment(0, 0, 1, "丙", "丙"),
                new SpeechSegment(1, 1, 1, "丁", "丁")
            ]);
        var service = CreateService(store, new FakeBookPlaybackMetadataQuery(), content);

        var statuses = await service.GetChapterCacheStatusesAsync(
            "book-1",
            [2, 0, 2, 1],
            CancellationToken.None);

        Assert.Equal([0, 1, 2], statuses.Select(status => status.ChapterIndex));
        Assert.Equal(new ChapterCacheStatus(0, 1, 1), statuses[0]);
        Assert.Equal(new ChapterCacheStatus(1, 0, null), statuses[1]);
        Assert.Equal(new ChapterCacheStatus(2, 0, 2), statuses[2]);
        Assert.Equal(1, content.BatchRequestCount);
        Assert.Equal(1, store.ValidityQueryCount);
    }

    [Fact]
    public void Changed_forwards_store_change_with_workspace_as_sender()
    {
        var store = new FakeAudioCacheStore();
        var service = CreateService(store, new FakeBookPlaybackMetadataQuery());
        object? sender = null;
        CacheChangedEventArgs? received = null;
        service.Changed += (eventSender, eventArgs) =>
        {
            sender = eventSender;
            received = eventArgs;
        };

        store.RaiseChanged("book-1", 3);

        Assert.Same(service, sender);
        Assert.Equal(new CacheChangedEventArgs("book-1", 3), received);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_reports_current_configuration_unavailable_for_expected_content_failures()
    {
        var service = CreateChapterService(new FakeBookPlaybackContentService
        {
            Exception = new FileNotFoundException("missing")
        });

        var chapter = Assert.Single(await service.GetCachedChaptersAsync("book-1", CancellationToken.None));

        Assert.Equal(0, chapter.CachedSegmentCount);
        Assert.Null(chapter.CurrentConfigurationSegmentCount);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_reports_unavailable_when_no_tts_rule_is_selected()
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult = [new CachedChapterStoreSummary("book-1", 0, 4, 4, 2048)]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(0, "第一章", "content.txt", 0, 4);
        var service = CreateService(store, metadata, ruleId: null);

        var chapter = Assert.Single(await service.GetCachedChaptersAsync("book-1", CancellationToken.None));

        Assert.Equal(0, chapter.CachedSegmentCount);
        Assert.Null(chapter.CurrentConfigurationSegmentCount);
        Assert.Empty(store.LastValidityQuery);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_propagates_cancellation_without_finishing_validity_checks()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new FakeAudioCacheStore
        {
            ChaptersResult = [new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024)],
            BeforeValidityQuery = cancellation.Cancel
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(0, "第一章", "content.txt", 0, 4);
        var content = new FakeBookPlaybackContentService();
        content.Chapters[("book-1", 0)] = PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [new SpeechSegment(0, 0, 1, "显示", "当前文本")]);
        var service = CreateService(store, metadata, content);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetCachedChaptersAsync("book-1", cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_propagates_unexpected_content_failures()
    {
        var service = CreateChapterService(new FakeBookPlaybackContentService
        {
            Exception = new ApplicationException("unexpected defect")
        });

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
        var service = CreateService(store, new FakeBookPlaybackMetadataQuery());

        await service.TrimToConfiguredLimitAsync(CancellationToken.None);
        var chapter = await service.ClearChapterAsync("book-1", 2, CancellationToken.None);
        var chapters = await service.ClearChaptersAsync("book-1", [3, 1, 3], CancellationToken.None);
        var book = await service.ClearBookAsync("book-1", CancellationToken.None);
        var all = await service.ClearAllAsync(CancellationToken.None);

        Assert.True(store.MaintenanceRequested);
        Assert.Equal(("book-1", 2), store.ClearedChapter);
        Assert.Equal(("book-1", new[] { 1, 3 }), store.ClearedChapters);
        Assert.Equal("book-1", store.ClearedBookId);
        Assert.True(store.ClearAllRequested);
        Assert.Equal(new CacheCleanupResult(8192, 4, 2, 1), chapter);
        Assert.Equal(chapter, chapters);
        Assert.Equal(chapter, book);
        Assert.Equal(chapter, all);
    }

    private static CacheWorkspaceService CreateChapterService(IBookPlaybackContentService content)
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult = [new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024)]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(0, "第一章", "content.txt", 0, 4);
        return CreateService(store, metadata, content);
    }

    private static CacheWorkspaceService CreateService(
        IAudioCacheStore store,
        IBookPlaybackMetadataQuery metadataQuery,
        IBookPlaybackContentService? content = null,
        long? ruleId = 7,
        int defaultSpeakSpeed = 10)
    {
        return new CacheWorkspaceService(
            store,
            metadataQuery,
            content ?? new FakeBookPlaybackContentService(),
            new FakeSelectedTtsRuleProvider(ruleId),
            new FakeAppSettingsService(defaultSpeakSpeed));
    }

    private sealed class FakeAudioCacheStore : IAudioCacheStore
    {
        public event EventHandler<CacheChangedEventArgs>? Changed;

        public AudioCacheStoreSummary SummaryResult { get; set; } =
            new(0, 0, AppSettings.DefaultCacheLimitBytes, false);

        public IReadOnlyList<CachedBookStoreSummary> BooksResult { get; set; } = [];

        public IReadOnlyList<CachedChapterStoreSummary> ChaptersResult { get; set; } = [];

        public AudioCacheStoreCleanupResult CleanupResult { get; set; } = new(0, 0, 0, 0);

        public IReadOnlySet<AudioCacheKey> ValidKeys { get; set; } = new HashSet<AudioCacheKey>();

        public IReadOnlyList<AudioCacheKey> LastValidityQuery { get; private set; } = [];

        public int ValidityQueryCount { get; private set; }

        public Action? BeforeValidityQuery { get; init; }

        public bool MaintenanceRequested { get; private set; }

        public (string BookId, int ChapterIndex)? ClearedChapter { get; private set; }

        public (string BookId, int[] ChapterIndices)? ClearedChapters { get; private set; }

        public string? ClearedBookId { get; private set; }

        public bool ClearAllRequested { get; private set; }

        public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(SummaryResult);

        public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) => Task.FromResult(BooksResult);

        public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult(ChaptersResult);

        public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
            IReadOnlyCollection<AudioCacheKey> keys,
            CancellationToken cancellationToken)
        {
            BeforeValidityQuery?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            ValidityQueryCount++;
            LastValidityQuery = keys.ToArray();
            return Task.FromResult<IReadOnlySet<AudioCacheKey>>(
                keys.Where(ValidKeys.Contains).ToHashSet());
        }

        public Task<AudioCacheStoreCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            ClearedChapter = (bookId, chapterIndex);
            return Task.FromResult(CleanupResult);
        }

        public Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            ClearedChapters = (bookId, chapterIndices.ToArray());
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

        public void RaiseChanged(string? bookId, int? chapterIndex)
        {
            Changed?.Invoke(this, new CacheChangedEventArgs(bookId, chapterIndex));
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

    private sealed class FakeBookPlaybackContentService : IBookPlaybackContentService
    {
        public Dictionary<(string BookId, int ChapterIndex), PlaybackChapterContent> Chapters { get; } = [];

        public Exception? Exception { get; init; }

        public int BatchRequestCount { get; private set; }

        public IReadOnlyList<int> LastBatchIndices { get; private set; } = [];

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Chapters.GetValueOrDefault((bookId, chapterIndex)));
        }

        public Task<IReadOnlyList<PlaybackChapterContent>> GetChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Exception is not null)
            {
                throw Exception;
            }

            BatchRequestCount++;
            LastBatchIndices = chapterIndices.ToArray();
            return Task.FromResult<IReadOnlyList<PlaybackChapterContent>>(
                chapterIndices
                    .Select(index => Chapters.GetValueOrDefault((bookId, index)))
                    .Where(chapter => chapter is not null)
                    .Cast<PlaybackChapterContent>()
                    .ToArray());
        }
    }

    private sealed class FakeSelectedTtsRuleProvider(long? ruleId) : ISelectedTtsRuleProvider
    {
        public Task<SelectedPlaybackRule?> GetSelectedRuleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                ruleId is null
                    ? null
                    : new SelectedPlaybackRule(
                        ruleId.Value,
                        "当前规则",
                        null!,
                        new NormalizedHttpTtsRule(
                            ruleId.Value,
                            "当前规则",
                            NormalizedTemplate.Parse($"https://cache-key.invalid/{ruleId.Value}"),
                            new Dictionary<string, NormalizedTemplate>(),
                            "GET",
                            null,
                            false,
                            "audio/mpeg",
                            null)));
        }

        public Task<SelectedPlaybackRule?> SelectRuleAsync(long selectedRuleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAppSettingsService(int defaultSpeakSpeed) : IAppSettingsService
    {
        public AppSettings Current { get; } = AppSettings.Default with
        {
            DefaultSpeakSpeed = defaultSpeakSpeed
        };

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
