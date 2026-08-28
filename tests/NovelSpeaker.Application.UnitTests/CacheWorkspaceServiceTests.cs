using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
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
    public async Task GetCachedChaptersAsync_projects_aggregate_coverage_for_the_current_playback_configuration()
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult =
            [
                new CachedChapterStoreSummary("book-1", 0, 9, 9, 4096)
            ],
            CoverageResult = [new ChapterCacheStatus(0, 1, 2)]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            4,
            "chapter-1");
        var service = CreateService(store, metadata, ruleId: 7, defaultSpeakSpeed: 12);

        var chapters = await service.GetCachedChaptersAsync("book-1", CancellationToken.None);

        var chapter = Assert.Single(chapters);
        Assert.Equal("第一章", chapter.Title);
        Assert.Equal(1, chapter.CachedSegmentCount);
        Assert.Equal(2, chapter.CurrentConfigurationSegmentCount);
        Assert.Equal("chapter-1", Assert.Single(store.LastCoverageQuery).ChapterId);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_batches_metadata_and_coverage_queries_for_all_cached_chapters()
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult =
            [
                new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024),
                new CachedChapterStoreSummary("book-1", 1, 1, 1, 1024)
            ],
            CoverageResult =
            [
                new ChapterCacheStatus(0, 1, 1),
                new ChapterCacheStatus(1, 1, 1)
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(0, "第一章", "content.txt", 0, 1, "chapter-1-0");
        metadata.Chapters[("book-1", 1)] = new PlaybackChapterMetadata(1, "第二章", "content.txt", 1, 1, "chapter-1-1");
        var service = CreateService(store, metadata);

        var chapters = await service.GetCachedChaptersAsync("book-1", CancellationToken.None);

        Assert.Equal(2, chapters.Count);
        Assert.Equal(1, store.CoverageQueryCount);
        Assert.Equal(2, store.LastCoverageQuery.Count);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_uses_persisted_plan_coverage_without_loading_chapter_content()
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult =
            [
                new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024)
            ],
            CoverageResult =
            [
                new ChapterCacheStatus(0, 2, 3)
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            2000,
            "chapter-1");
        var service = CreateService(store, metadata, readChapterTitle: true);

        var chapters = await service.GetCachedChaptersAsync("book-1", CancellationToken.None);

        var chapter = Assert.Single(chapters);
        Assert.Equal(2, chapter.CachedSegmentCount);
        Assert.Equal(3, chapter.CurrentConfigurationSegmentCount);
        Assert.Equal(1, store.CoverageQueryCount);
        var query = Assert.Single(store.LastCoverageQuery);
        Assert.Equal("chapter-1", query.ChapterId);
        Assert.True(query.ReadChapterTitle);
        Assert.Equal(Fingerprint.Sha256("第一章"), query.ChapterTitleSpeechTextHash);
        Assert.NotNull(query.TextProfileFingerprint);
    }

    [Fact]
    public async Task GetChapterCacheStatusesAsync_returns_all_requested_chapters_in_order()
    {
        var store = new FakeAudioCacheStore
        {
            CoverageResult =
            [
                new ChapterCacheStatus(0, 1, 1),
                new ChapterCacheStatus(2, 0, 2)
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(0, "第一章", "content.txt", 0, 1, "chapter-1-0");
        metadata.Chapters[("book-1", 2)] = new PlaybackChapterMetadata(2, "第三章", "content.txt", 0, 1, "chapter-1-2");
        var service = CreateService(store, metadata);

        var statuses = await service.GetChapterCacheStatusesAsync(
            "book-1",
            [2, 0, 2, 1],
            CancellationToken.None);

        Assert.Equal([0, 1, 2], statuses.Select(status => status.ChapterIndex));
        Assert.Equal(new ChapterCacheStatus(0, 1, 1), statuses[0]);
        Assert.Equal(new ChapterCacheStatus(1, 0, null), statuses[1]);
        Assert.Equal(new ChapterCacheStatus(2, 0, 2), statuses[2]);
        Assert.Equal(1, store.CoverageQueryCount);
    }

    [Fact]
    public async Task GetChapterCacheStatusesAsync_returns_stale_status_then_refreshes_plan_in_background()
    {
        var store = new FakeAudioCacheStore
        {
            CoverageResult =
            [
                new ChapterCacheStatus(0, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanStale
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        var content = new BlockingBookPlaybackContentService();
        using var service = CreateService(store, metadata, bookContentService: content);
        var changed = new TaskCompletionSource<CacheChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.Changed += (_, eventArgs) => changed.TrySetResult(eventArgs);

        var statuses = await service.GetChapterCacheStatusesAsync(
            "book-1",
            [0],
            CancellationToken.None);

        Assert.Equal(ChapterCacheStatusKind.PlanStale, Assert.Single(statuses).Kind);
        Assert.Equal(("book-1", 0), await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(1)));

        content.Release.TrySetResult();

        Assert.Equal(
            new CacheChangedEventArgs("book-1", 0),
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Concurrent_stale_status_queries_share_one_background_plan_refresh()
    {
        var store = new FakeAudioCacheStore
        {
            CoverageResult =
            [
                new ChapterCacheStatus(0, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanStale
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        var content = new BlockingBookPlaybackContentService();
        using var service = CreateService(store, metadata, bookContentService: content);

        await service.GetChapterCacheStatusesAsync("book-1", [0], CancellationToken.None);
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await service.GetChapterCacheStatusesAsync("book-1", [0], CancellationToken.None);

        Assert.Equal(1, content.RequestCount);
        content.Release.TrySetResult();
    }

    [Fact]
    public async Task Background_plan_refresh_rechecks_text_configuration_before_publishing_completion()
    {
        var store = new FakeAudioCacheStore
        {
            CoverageResult =
            [
                new ChapterCacheStatus(0, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanStale
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        var settings = new MutableAppSettingsService();
        var rules = new MutableRegexReplacementRuleRepository();
        var planStore = new RecordingChapterSpeechPlanStore();
        TextProfileFingerprint CurrentProfile() => TextProfileFingerprint.Create(
            settings.Current.ToTextSegmentationOptions(),
            rules.Rules);
        var content = new ConfigurationAwareBookPlaybackContentService(
            () => settings.Current.LongParagraphThreshold,
            blockFirstRequest: true,
            currentProfile: CurrentProfile,
            speechPlanStore: planStore,
            chapterId: "chapter-1-0");
        using var service = CreateService(
            store,
            metadata,
            bookContentService: content,
            settingsService: settings,
            regexRuleRepository: rules,
            speechPlanStore: planStore);
        var changed = new TaskCompletionSource<CacheChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.Changed += (_, eventArgs) => changed.TrySetResult(eventArgs);

        await service.GetChapterCacheStatusesAsync("book-1", [0], CancellationToken.None);
        Assert.Equal(100, await content.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)));

        settings.SetLongParagraphThreshold(200);
        rules.SetRules(
        [
            new RegexReplacementRule(
                Guid.NewGuid(),
                "fixture-rule",
                true,
                10,
                "正文",
                "替换",
                RegexReplacementScope.Speech,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)
        ]);
        content.ReleaseFirstRequest.TrySetResult();

        Assert.Equal(
            new CacheChangedEventArgs("book-1", 0),
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal([100, 200], content.ObservedConfigurations);

        var currentPlan = await planStore.GetAsync("chapter-1-0", CancellationToken.None);
        Assert.NotNull(currentPlan);
        Assert.Equal(CurrentProfile(), currentPlan!.TextProfileFingerprint);
        Assert.Equal(2, planStore.SavedPlans.Count);
        Assert.NotEqual(
            planStore.SavedPlans[0].TextProfileFingerprint,
            currentPlan.TextProfileFingerprint);
    }

    [Fact]
    public async Task Shutdown_during_final_plan_recheck_does_not_publish_chapter_refresh()
    {
        var store = new FakeAudioCacheStore
        {
            CoverageResult =
            [
                new ChapterCacheStatus(0, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanStale
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        var settings = new MutableAppSettingsService();
        var planStore = new RecordingChapterSpeechPlanStore();
        var rules = new BlockingStableProfileRepository(planStore);
        TextProfileFingerprint CurrentProfile() => TextProfileFingerprint.Create(
            settings.Current.ToTextSegmentationOptions(),
            rules.Rules);
        var content = new ConfigurationAwareBookPlaybackContentService(
            () => settings.Current.LongParagraphThreshold,
            blockFirstRequest: false,
            currentProfile: CurrentProfile,
            speechPlanStore: planStore,
            chapterId: "chapter-1-0");
        using var service = CreateService(
            store,
            metadata,
            bookContentService: content,
            settingsService: settings,
            regexRuleRepository: rules,
            speechPlanStore: planStore);
        var changed = new TaskCompletionSource<CacheChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.Changed += (_, eventArgs) => changed.TrySetResult(eventArgs);

        await service.GetChapterCacheStatusesAsync("book-1", [0], CancellationToken.None);
        await rules.StableProfileReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await service.StopBackgroundOperationsAsync(CancellationToken.None);

        Assert.False(changed.Task.IsCompleted);
        Assert.True(rules.CancellationObserved.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Failed_background_plan_refresh_is_logged_and_can_be_retried()
    {
        var store = new FakeAudioCacheStore
        {
            CoverageResult =
            [
                new ChapterCacheStatus(0, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanStale
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        var content = new RetryableBookPlaybackContentService();
        var reporter = new RecordingCacheWorkspaceFailureReporter();
        using var service = CreateService(
            store,
            metadata,
            bookContentService: content,
            failureReporter: reporter);
        var changed = new TaskCompletionSource<CacheChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.Changed += (_, eventArgs) => changed.TrySetResult(eventArgs);

        await service.GetChapterCacheStatusesAsync("book-1", [0], CancellationToken.None);
        await content.FirstAttemptCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await reporter.Reported.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal([typeof(FileNotFoundException)], reporter.ExceptionTypes);

        await service.GetChapterCacheStatusesAsync("book-1", [0], CancellationToken.None);

        Assert.Equal(
            new CacheChangedEventArgs("book-1", 0),
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(2, content.RequestCount);
    }

    [Fact]
    public async Task GetChapterCacheStatusesAsync_does_not_build_missing_plan_for_directory_query()
    {
        var store = new FakeAudioCacheStore
        {
            CoverageResult =
            [
                new ChapterCacheStatus(0, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanMissing
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        var content = new BlockingBookPlaybackContentService();
        using var service = CreateService(store, metadata, bookContentService: content);

        var statuses = await service.GetChapterCacheStatusesAsync(
            "book-1",
            [0],
            CancellationToken.None);

        Assert.Equal(ChapterCacheStatusKind.PlanMissing, Assert.Single(statuses).Kind);
        Assert.Equal(0, content.RequestCount);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_builds_missing_plan_for_cached_chapter_in_background()
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult = [new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024)],
            CoverageResult =
            [
                new ChapterCacheStatus(0, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanMissing
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        var content = new BlockingBookPlaybackContentService();
        using var service = CreateService(store, metadata, bookContentService: content);

        var chapters = await service.GetCachedChaptersAsync("book-1", CancellationToken.None);

        Assert.Equal(ChapterCacheStatusKind.PlanMissing, Assert.Single(chapters).CurrentConfigurationStatus);
        Assert.Equal(("book-1", 0), await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        content.Release.TrySetResult();
    }

    [Fact]
    public async Task GetCachedChaptersAsync_refreshes_only_cached_target_chapters()
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult = [new CachedChapterStoreSummary("book-1", 2, 1, 1, 1024)],
            CoverageResult =
            [
                new ChapterCacheStatus(2, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanStale
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        metadata.Chapters[("book-1", 1)] = new PlaybackChapterMetadata(
            1,
            "第二章",
            "content.txt",
            1,
            1,
            "chapter-1-1");
        metadata.Chapters[("book-1", 2)] = new PlaybackChapterMetadata(
            2,
            "第三章",
            "content.txt",
            2,
            1,
            "chapter-1-2");
        var content = new BlockingBookPlaybackContentService();
        using var service = CreateService(store, metadata, bookContentService: content);

        var chapters = await service.GetCachedChaptersAsync("book-1", CancellationToken.None);

        Assert.Equal(ChapterCacheStatusKind.PlanStale, Assert.Single(chapters).CurrentConfigurationStatus);
        Assert.Equal([2], Assert.Single(metadata.RequestedChapterIndexBatches));
        Assert.Equal(("book-1", 2), await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        content.Release.TrySetResult();
    }

    [Fact]
    public async Task StopBackgroundOperationsAsync_cancels_and_waits_for_owned_plan_refreshes()
    {
        var store = new FakeAudioCacheStore
        {
            CoverageResult =
            [
                new ChapterCacheStatus(0, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanStale
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        var content = new BlockingBookPlaybackContentService();
        using var service = CreateService(store, metadata, bookContentService: content);

        await service.GetChapterCacheStatusesAsync("book-1", [0], CancellationToken.None);
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await service.StopBackgroundOperationsAsync(CancellationToken.None);
        await service.StopBackgroundOperationsAsync(CancellationToken.None);

        Assert.True(content.CancellationObserved.Task.IsCompletedSuccessfully);
        service.Dispose();
        await service.StopBackgroundOperationsAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Canceled_shutdown_wait_observes_noncooperative_refresh_without_publishing()
    {
        var store = new FakeAudioCacheStore
        {
            CoverageResult =
            [
                new ChapterCacheStatus(0, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanStale
                }
            ]
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            1,
            "chapter-1-0");
        var content = new NonCooperativeBookPlaybackContentService();
        using var service = CreateService(store, metadata, bookContentService: content);
        var changed = new TaskCompletionSource<CacheChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.Changed += (_, eventArgs) => changed.TrySetResult(eventArgs);

        await service.GetChapterCacheStatusesAsync("book-1", [0], CancellationToken.None);
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using var shutdownCancellation = new CancellationTokenSource();
        var stop = service.StopBackgroundOperationsAsync(shutdownCancellation.Token);
        shutdownCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);

        content.Release.TrySetResult();
        await service.StopBackgroundOperationsAsync(CancellationToken.None);

        Assert.False(changed.Task.IsCompleted);
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
    public async Task GetCachedChaptersAsync_reports_configuration_unavailable_for_expected_metadata_failures()
    {
        var service = CreateChapterService(new FileNotFoundException("missing"));

        var chapter = Assert.Single(await service.GetCachedChaptersAsync("book-1", CancellationToken.None));

        Assert.Equal(0, chapter.CachedSegmentCount);
        Assert.Null(chapter.CurrentConfigurationSegmentCount);
        Assert.Equal(ChapterCacheStatusKind.ConfigurationUnavailable, chapter.CurrentConfigurationStatus);
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
        Assert.Equal(ChapterCacheStatusKind.ConfigurationUnavailable, chapter.CurrentConfigurationStatus);
        Assert.Empty(store.LastCoverageQuery);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_propagates_cancellation_without_finishing_coverage_query()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new FakeAudioCacheStore
        {
            ChaptersResult = [new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024)],
            BeforeCoverageQuery = cancellation.Cancel
        };
        var metadata = new FakeBookPlaybackMetadataQuery();
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            4,
            "chapter-1");
        var service = CreateService(store, metadata);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetCachedChaptersAsync("book-1", cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task GetCachedChaptersAsync_propagates_unexpected_metadata_failures()
    {
        var service = CreateChapterService(new ApplicationException("unexpected defect"));

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

    private static CacheWorkspaceService CreateChapterService(Exception exception)
    {
        var store = new FakeAudioCacheStore
        {
            ChaptersResult = [new CachedChapterStoreSummary("book-1", 0, 1, 1, 1024)]
        };
        var metadata = new FakeBookPlaybackMetadataQuery
        {
            Exception = exception
        };
        metadata.Chapters[("book-1", 0)] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            4,
            "chapter-1");
        return CreateService(store, metadata);
    }

    private static CacheWorkspaceService CreateService(
        IAudioCacheStore store,
        IBookPlaybackMetadataQuery metadataQuery,
        long? ruleId = 7,
        int defaultSpeakSpeed = 10,
        bool readChapterTitle = false,
        IBookPlaybackContentService? bookContentService = null,
        IAppSettingsService? settingsService = null,
        IRegexReplacementRuleRepository? regexRuleRepository = null,
        ICacheWorkspaceFailureReporter? failureReporter = null,
        IChapterSpeechPlanStore? speechPlanStore = null)
    {
        return new CacheWorkspaceService(
            store,
            metadataQuery,
            new FakeSelectedTtsRuleProvider(ruleId),
            settingsService ?? new FakeAppSettingsService(defaultSpeakSpeed, readChapterTitle),
            failureReporter: failureReporter,
            bookContentService: bookContentService,
            regexRuleRepository: regexRuleRepository,
            speechPlanStore: speechPlanStore);
    }

    private sealed class FakeAudioCacheStore : IAudioCacheStore
    {
        public event EventHandler<CacheChangedEventArgs>? Changed;

        public AudioCacheStoreSummary SummaryResult { get; set; } =
            new(0, 0, AppSettings.DefaultCacheLimitBytes, false);

        public IReadOnlyList<CachedBookStoreSummary> BooksResult { get; set; } = [];

        public IReadOnlyList<CachedChapterStoreSummary> ChaptersResult { get; set; } = [];

        public AudioCacheStoreCleanupResult CleanupResult { get; set; } = new(0, 0, 0, 0);

        public IReadOnlyList<ChapterCacheStatus> CoverageResult { get; set; } = [];

        public IReadOnlyList<CurrentCacheChapterQuery> LastCoverageQuery { get; private set; } = [];

        public int CoverageQueryCount { get; private set; }

        public Action? BeforeCoverageQuery { get; init; }

        public bool MaintenanceRequested { get; private set; }

        public bool StartupMaintenanceRequested { get; private set; }

        public (string BookId, int ChapterIndex)? ClearedChapter { get; private set; }

        public (string BookId, int[] ChapterIndices)? ClearedChapters { get; private set; }

        public string? ClearedBookId { get; private set; }

        public bool ClearAllRequested { get; private set; }

        public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(SummaryResult);

        public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) => Task.FromResult(BooksResult);

        public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult(ChaptersResult);

        public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
            IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
            SynthesisProfileFingerprint synthesisProfile,
            CancellationToken cancellationToken)
        {
            BeforeCoverageQuery?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            CoverageQueryCount++;
            LastCoverageQuery = chapters.ToArray();
            return Task.FromResult(CoverageResult);
        }

        public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
            IReadOnlyCollection<AudioCacheKey> keys,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Current completeness must use the aggregate coverage query.");

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

        public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken)
        {
            StartupMaintenanceRequested = true;
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

        public List<int[]> RequestedChapterIndexBatches { get; } = [];

        public Exception? Exception { get; init; }

        public Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Exception is not null)
            {
                throw Exception;
            }

            RequestedBookIds.Add(bookId);
            return Task.FromResult(Books.GetValueOrDefault(bookId));
        }

        public Task<PlaybackChapterMetadata?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Chapters.GetValueOrDefault((bookId, chapterIndex)));
        }

        public Task<IReadOnlyList<PlaybackChapterMetadata>> GetChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Exception is not null)
            {
                throw Exception;
            }

            var requested = chapterIndices.Distinct().Order().ToArray();
            RequestedChapterIndexBatches.Add(requested);
            return Task.FromResult<IReadOnlyList<PlaybackChapterMetadata>>(
                requested
                    .Select(index => Chapters.GetValueOrDefault((bookId, index)))
                    .Where(static chapter => chapter is not null)
                    .Select(static chapter => chapter!)
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

    private sealed class FakeAppSettingsService(int defaultSpeakSpeed, bool readChapterTitle) : IAppSettingsService
    {
        public AppSettings Current { get; } = AppSettings.Default with
        {
            DefaultSpeakSpeed = defaultSpeakSpeed,
            ReadChapterTitle = readChapterTitle
        };

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingBookPlaybackContentService : IBookPlaybackContentService
    {
        public TaskCompletionSource<(string BookId, int ChapterIndex)> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount { get; private set; }

        public Task<PlaybackBookContent?> GetBookAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Started.TrySetResult((bookId, chapterIndex));
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                throw;
            }

            return PlaybackChapterContent.FromLoaded(
                chapterIndex,
                $"第 {chapterIndex + 1} 章",
                [],
                $"chapter-{chapterIndex}");
        }
    }

    private sealed class ConfigurationAwareBookPlaybackContentService(
        Func<int> currentConfiguration,
        bool blockFirstRequest,
        Func<TextProfileFingerprint>? currentProfile = null,
        IChapterSpeechPlanStore? speechPlanStore = null,
        string chapterId = "chapter-0") : IBookPlaybackContentService
    {
        public TaskCompletionSource<int> FirstRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<int> ObservedConfigurations { get; } = [];

        public int RequestCount { get; private set; }

        public Task<PlaybackBookContent?> GetBookAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var configuration = currentConfiguration();
            var profile = currentProfile?.Invoke();
            ObservedConfigurations.Add(configuration);
            if (FirstRequestStarted.TrySetResult(configuration) && blockFirstRequest)
            {
                await ReleaseFirstRequest.Task.WaitAsync(cancellationToken);
            }

            if (speechPlanStore is not null && profile is not null)
            {
                await speechPlanStore.SaveAsync(
                    CreateReadyPlan(chapterId, profile, configuration),
                    cancellationToken);
            }

            return PlaybackChapterContent.FromLoaded(
                chapterIndex,
                $"第 {chapterIndex + 1} 章",
                [],
                chapterId);
        }
    }

    private sealed class NonCooperativeBookPlaybackContentService : IBookPlaybackContentService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PlaybackBookContent?> GetBookAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
            return PlaybackChapterContent.FromLoaded(
                chapterIndex,
                $"第 {chapterIndex + 1} 章",
                [],
                $"chapter-{chapterIndex}");
        }
    }

    private sealed class RecordingChapterSpeechPlanStore : IChapterSpeechPlanStore
    {
        private readonly Dictionary<string, ChapterSpeechPlan> _plans = [];

        public List<ChapterSpeechPlan> SavedPlans { get; } = [];

        public bool PlanWasRead { get; private set; }

        public Task<ChapterSpeechPlan?> GetAsync(
            string chapterId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlanWasRead = true;
            return Task.FromResult(_plans.GetValueOrDefault(chapterId));
        }

        public Task SaveAsync(ChapterSpeechPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedPlans.Add(plan);
            _plans[plan.ChapterId] = plan;
            return Task.CompletedTask;
        }

        public Task<int> DeletePlansWithoutCacheEntriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class BlockingStableProfileRepository(
        RecordingChapterSpeechPlanStore planStore) : IRegexReplacementRuleRepository
    {
        private readonly MutableRegexReplacementRuleRepository _inner = new();
        private int _gateEntered;

        public TaskCompletionSource StableProfileReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStableProfile { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<RegexReplacementRule> Rules => _inner.Rules;

        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(
            CancellationToken cancellationToken)
        {
            if (planStore.PlanWasRead && Interlocked.Exchange(ref _gateEntered, 1) == 0)
            {
                return WaitForStableProfileAsync(cancellationToken);
            }

            return Task.FromResult(_inner.Rules);
        }

        public void SetRules(IReadOnlyList<RegexReplacementRule> rules) => _inner.SetRules(rules);

        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveOrderAsync(
            IReadOnlyList<(Guid RuleId, int SortOrder)> order,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private async Task<IReadOnlyList<RegexReplacementRule>> WaitForStableProfileAsync(
            CancellationToken cancellationToken)
        {
            StableProfileReadStarted.TrySetResult();
            try
            {
                await ReleaseStableProfile.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                throw;
            }

            return _inner.Rules;
        }
    }

    private static ChapterSpeechPlan CreateReadyPlan(
        string chapterId,
        TextProfileFingerprint profile,
        int configuration) =>
        new(
            chapterId,
            Fingerprint.Sha256($"revision-{configuration}"),
            profile,
            Fingerprint.Sha256($"output-{configuration}"),
            ChapterSpeechPlanState.Ready,
            0,
            DateTimeOffset.UtcNow,
            []);

    private sealed class RetryableBookPlaybackContentService : IBookPlaybackContentService
    {
        public TaskCompletionSource FirstAttemptCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount { get; private set; }

        public Task<PlaybackBookContent?> GetBookAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            if (RequestCount == 1)
            {
                FirstAttemptCompleted.TrySetResult();
                throw new FileNotFoundException(@"C:\private\novel-content.txt");
            }

            return Task.FromResult<PlaybackChapterContent?>(
                PlaybackChapterContent.FromLoaded(
                    chapterIndex,
                    $"第 {chapterIndex + 1} 章",
                    [],
                    $"chapter-{chapterIndex}"));
        }
    }

    private sealed class MutableAppSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default with
        {
            LongParagraphThreshold = 100
        };

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void SetLongParagraphThreshold(int value)
        {
            Current = Current with { LongParagraphThreshold = value };
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class MutableRegexReplacementRuleRepository : IRegexReplacementRuleRepository
    {
        private IReadOnlyList<RegexReplacementRule> _rules = [];

        public IReadOnlyList<RegexReplacementRule> Rules => _rules;

        public void SetRules(IReadOnlyList<RegexReplacementRule> rules) => _rules = rules;

        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_rules);

        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingCacheWorkspaceFailureReporter : ICacheWorkspaceFailureReporter
    {
        public List<Type> ExceptionTypes { get; } = [];

        public TaskCompletionSource Reported { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReportCompletenessUnavailable(Exception exception)
        {
            ExceptionTypes.Add(exception.GetType());
            Reported.TrySetResult();
        }
    }
}
