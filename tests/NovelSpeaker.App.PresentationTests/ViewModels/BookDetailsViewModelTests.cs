using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using System.Collections.Specialized;
using System.ComponentModel;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Features.Books.Library;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class BookDetailsViewModelTests
{
    private async Task LoadAsync_projects_read_only_fields_and_chapters()
    {
        var viewModel = CreateViewModel();

        await LoadViewModelAsync(viewModel);

        Assert.Equal("示例小说", viewModel.Title);
        Assert.Equal("作者甲", viewModel.DisplayAuthor);
        Assert.Equal("共 3 章", viewModel.TotalChapterCountText);
        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.Equal("共 3 章 · 当前第 2 章", viewModel.ChapterCatalogSummaryText);
        Assert.Contains("67", viewModel.ProgressText);
        Assert.Equal("2 KB", viewModel.CacheSizeText);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.True(viewModel.Chapters[1].IsCurrent);
        Assert.Equal("第二章 继续", viewModel.Chapters[1].TitleToolTip);
    }

    private async Task Playback_snapshot_updates_details_and_catalog_current_item()
    {
        var playbackCoordinator = new FakePlaybackCoordinator();
        var viewModel = CreateViewModel(playbackCoordinator: playbackCoordinator);

        await LoadViewModelAsync(viewModel);

        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 2,
                ChapterTitle = "第三章 结尾",
                SegmentIndex = 1,
                SegmentCount = 2
            });

        Assert.Equal("第三章 结尾", viewModel.CurrentChapterText);
        Assert.Equal("共 3 章 · 当前第 3 章", viewModel.ChapterCatalogSummaryText);
        Assert.Equal(1d, viewModel.ProgressRatio);
        Assert.False(viewModel.Chapters[1].IsCurrent);
        Assert.True(viewModel.Chapters[2].IsCurrent);
        Assert.Same(viewModel.Chapters[2], viewModel.CurrentChapterItem);

        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-2",
                ChapterIndex = 0,
                ChapterTitle = "其它书籍第一章"
            });

        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.True(viewModel.Chapters[1].IsCurrent);
        Assert.Equal(2d / 3d, viewModel.ProgressRatio);

        playbackCoordinator.Publish(PlaybackSnapshot.Idle);

        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.True(viewModel.Chapters[1].IsCurrent);
    }

    private async Task Late_details_result_preserves_newer_playback_snapshot()
    {
        var managementService = new FakeBookManagementService
        {
            BlockDetailsLoad = true
        };
        var playbackCoordinator = new FakePlaybackCoordinator();
        var viewModel = CreateViewModel(
            managementService: managementService,
            playbackCoordinator: playbackCoordinator);

        viewModel.HandleNavigatedTo();
        var loadTask = viewModel.LoadAsync("book-1", CancellationToken.None);
        await Task.Yield();

        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 2,
                ChapterTitle = "第三章 结尾"
            });

        managementService.ReleaseBlockedDetailsLoad();
        await loadTask;
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 3);

        Assert.Equal("第三章 结尾", viewModel.CurrentChapterText);
        Assert.False(viewModel.Chapters[1].IsCurrent);
        Assert.True(viewModel.Chapters[2].IsCurrent);
        Assert.Equal(1d, viewModel.ProgressRatio);
    }

    private async Task Snapshot_after_navigation_away_does_not_update_details()
    {
        var playbackCoordinator = new FakePlaybackCoordinator();
        var viewModel = CreateViewModel(playbackCoordinator: playbackCoordinator);

        await LoadViewModelAsync(viewModel);
        viewModel.HandleNavigatedFrom();

        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 2,
                ChapterTitle = "第三章 结尾"
            });

        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.True(viewModel.Chapters[1].IsCurrent);
        Assert.False(viewModel.Chapters[2].IsCurrent);
    }

    private async Task Queued_stale_playback_snapshot_is_ignored_after_page_leave()
    {
        var uiScheduler = new QueuedPlaybackUiScheduler();
        var playbackCoordinator = new FakePlaybackCoordinator();
        var viewModel = CreateViewModel(
            playbackCoordinator: playbackCoordinator,
            uiScheduler: uiScheduler);

        await LoadViewModelAsync(viewModel);
        uiScheduler.QueueActions = true;
        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 2,
                ChapterTitle = "第三章 结尾"
            });

        viewModel.HandleNavigatedFrom();
        uiScheduler.RunAll();

        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.True(viewModel.Chapters[1].IsCurrent);
        Assert.False(viewModel.Chapters[2].IsCurrent);
    }

    private async Task LoadAsync_returns_after_critical_catalog_and_stages_statistics()
    {
        var managementService = new FakeBookManagementService();
        var viewModel = CreateViewModel(managementService: managementService);

        await viewModel.LoadAsync("book-1", CancellationToken.None);

        Assert.Equal("示例小说", viewModel.Title);
        Assert.Equal("作者甲", viewModel.DisplayAuthor);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.True(viewModel.IsChapterCatalogReady);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(1, managementService.GetBookDetailsHeaderCallCount);
        Assert.Equal(1, managementService.GetBookDetailsCallCount);
        Assert.Equal(0, managementService.GetBookDetailsStatisticsCallCount);

        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => viewModel.Chapters.Count == 3 && !viewModel.IsBusy);

        Assert.Equal("共 3 章", viewModel.TotalChapterCountText);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.Equal(1, managementService.GetBookDetailsStatisticsCallCount);
        Assert.False(viewModel.IsBusy);
    }

    private async Task Starting_a_new_load_clears_the_previous_catalog_before_details_finish()
    {
        var managementService = new FakeBookManagementService();
        var viewModel = CreateViewModel(managementService: managementService);

        await LoadViewModelAsync(viewModel);
        managementService.BlockDetailsLoad = true;

        var loadTask = viewModel.LoadAsync("book-1", CancellationToken.None);
        await Task.Yield();

        Assert.Empty(viewModel.Chapters);
        Assert.Null(viewModel.CurrentChapterItem);

        managementService.ReleaseBlockedDetailsLoad();
        await loadTask;
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 3);
    }

    private async Task Fast_leave_and_reenter_cancels_old_staged_load()
    {
        var managementService = new FakeBookManagementService
        {
            BlockStatisticsLoad = true
        };
        var viewModel = CreateViewModel(managementService: managementService);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await Task.Yield();

        viewModel.HandleNavigatedFrom();
        managementService.BlockStatisticsLoad = false;
        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();

        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 3);

        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.True(viewModel.Chapters[1].IsCurrent);
    }

    private async Task Staged_statistics_cannot_release_commands_during_cache_clear()
    {
        var managementService = new FakeBookManagementService
        {
            BlockStatisticsLoad = true
        };
        var cacheDependencies = new FakeCacheDetailsDependencies
        {
            BlockClearBook = true
        };
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Confirm
        };
        var viewModel = CreateViewModel(
            managementService: managementService,
            cacheDependencies: cacheDependencies,
            dialogService: dialogService);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await Task.Yield();

        managementService.BlockStatisticsLoad = false;
        var clearTask = viewModel.ClearCacheCommand.ExecuteAsync(null);
        await Task.Yield();
        managementService.ReleaseBlockedStatisticsLoad();
        await Task.Yield();

        Assert.True(viewModel.IsBusy);

        cacheDependencies.ReleaseBlockedClearBook();
        await clearTask;

        Assert.False(viewModel.IsBusy);
    }

    private async Task Cache_clear_ignores_stale_staged_statistics()
    {
        var managementService = new FakeBookManagementService
        {
            BlockStatisticsLoad = true,
            NextDetailsAfterClear = CreateDetails(cachedAudioBytes: 512)
        };
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Confirm
        };
        var viewModel = CreateViewModel(
            managementService: managementService,
            dialogService: dialogService);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await Task.Yield();

        managementService.BlockStatisticsLoad = false;
        await viewModel.ClearCacheCommand.ExecuteAsync(null);
        Assert.Equal("512 B", viewModel.CacheSizeText);

        managementService.ReleaseBlockedStatisticsLoad();
        await Task.Yield();

        Assert.Equal("512 B", viewModel.CacheSizeText);
    }

    private async Task Loading_a_10000_chapter_catalog_uses_batched_collection_projection()
    {
        var managementService = new FakeBookManagementService
        {
            Details = CreateDetails(10_000, 9_999)
        };
        var viewModel = CreateViewModel(managementService: managementService);
        var changes = new List<NotifyCollectionChangedAction>();
        viewModel.Chapters.CollectionChanged += (_, args) => changes.Add(args.Action);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 10_000);

        Assert.Equal(10_000, viewModel.Chapters.Count);
        Assert.NotEmpty(changes);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Add, changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, changes[^1]);
        Assert.Same(viewModel.Chapters[9_999], viewModel.CurrentChapterItem);
    }

    [Fact]
    public async Task Book_details_loading_and_playback_contracts_are_isolated()
    {
        await LoadAsync_projects_read_only_fields_and_chapters();
        await Playback_snapshot_updates_details_and_catalog_current_item();
        await Late_details_result_preserves_newer_playback_snapshot();
        await Snapshot_after_navigation_away_does_not_update_details();
        await Queued_stale_playback_snapshot_is_ignored_after_page_leave();
    }

    [Fact]
    public async Task Book_details_loading_lifecycle_contracts_are_isolated()
    {
        await LoadAsync_returns_after_critical_catalog_and_stages_statistics();
        await Starting_a_new_load_clears_the_previous_catalog_before_details_finish();
        await Fast_leave_and_reenter_cancels_old_staged_load();
        await Staged_statistics_cannot_release_commands_during_cache_clear();
        await Cache_clear_ignores_stale_staged_statistics();
    }

    [Fact]
    public Task Book_details_large_catalog_projection_is_batched() =>
        Loading_a_10000_chapter_catalog_uses_batched_collection_projection();

    private async Task SelectChapterCommand_navigates_to_player_with_first_segment_after_confirming_unsaved_changes()
    {
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Discard
        };
        var guardedNavigationService = new FakeGuardedNavigationService();
        var viewModel = CreateViewModel(
            dialogService: dialogService,
            guardedNavigationService: guardedNavigationService);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "待保存的新标题";

        await viewModel.SelectChapterCommand.ExecuteAsync(viewModel.Chapters[2]);

        Assert.Equal("示例小说", viewModel.EditTitle);
        var request = Assert.IsType<PlayerNavigationRequest>(guardedNavigationService.LastNavigationRoute);
        Assert.Equal("book-1", request.BookId);
        Assert.Equal(new BookDetailsRoute("book-1"), request.ReturnRoute);
        Assert.Equal(2, request.ChapterIndex);
        Assert.Equal(0, request.SegmentIndex);
    }

    [Fact]
    public Task Selecting_a_chapter_opens_playback_at_its_first_segment_after_unsaved_changes() =>
        SelectChapterCommand_navigates_to_player_with_first_segment_after_confirming_unsaved_changes();

    private static BookDetailsViewModel CreateViewModel(
        FakeBookManagementService? managementService = null,
        FakeCacheDetailsDependencies? cacheDependencies = null,
        FakeAppSettingsService? settingsService = null,
        IAppFeedbackService? feedbackService = null,
        FakeAppDialogService? dialogService = null,
        FakeBookDeleteDialogService? deleteDialogService = null,
        FakePlaybackCoordinator? playbackCoordinator = null,
        FakeGuardedNavigationService? guardedNavigationService = null,
        IBookCatalogInvalidationState? invalidationState = null,
        IUiScheduler? uiScheduler = null)
    {
        managementService ??= new FakeBookManagementService();
        cacheDependencies ??= new FakeCacheDetailsDependencies();
        return new BookDetailsViewModel(
            managementService,
            managementService,
            managementService,
            cacheDependencies,
            cacheDependencies,
            cacheDependencies,
            settingsService ?? new FakeAppSettingsService(),
            new BookCoverGenerator(),
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            deleteDialogService ?? new FakeBookDeleteDialogService(),
            invalidationState ?? new BookCatalogInvalidationState(),
            playbackCoordinator ?? new FakePlaybackCoordinator(),
            guardedNavigationService ?? new FakeGuardedNavigationService(),
            uiScheduler ?? new ImmediateUiScheduler());
    }

    private static FakeDetailsState CreateDetails(
        string title = "示例小说",
        string? author = "作者甲",
        long cachedAudioBytes = 2048)
    {
        return new FakeDetailsState(
            new BookDetailsHeader("book-1", title, author),
            [
                new BookChapterSummary(0, "第一章 开始", 0, 120),
                new BookChapterSummary(1, "第二章 继续", 120, 180),
                new BookChapterSummary(2, "第三章 结尾", 300, 90)
            ],
            new BookReadingPosition("book-1", 1, 0, 0, 0, DateTimeOffset.UtcNow),
            new BookDetailsStatistics(cachedAudioBytes));
    }

    private static FakeDetailsState CreateDetails(int chapterCount, int currentChapterIndex)
    {
        return new FakeDetailsState(
            new BookDetailsHeader("book-1", "示例小说", "作者甲"),
            Enumerable.Range(0, chapterCount)
                .Select(index => new BookChapterSummary(
                    index,
                    $"第 {index + 1} 章 标题",
                    index * 100,
                    100))
                .ToArray(),
            new BookReadingPosition("book-1", currentChapterIndex, 0, 0, 0, DateTimeOffset.UtcNow),
            new BookDetailsStatistics(2048));
    }

    private static async Task WaitForConditionAsync(
        BookDetailsViewModel viewModel,
        Func<bool> predicate)
    {
        if (predicate())
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, _) =>
        {
            if (predicate())
            {
                completion.TrySetResult();
            }
        };
        viewModel.PropertyChanged += handler;
        try
        {
            if (predicate())
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
        }
    }

    private static async Task LoadViewModelAsync(BookDetailsViewModel viewModel)
    {
        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 3);
    }

    private sealed record FakeDetailsState(
        BookDetailsHeader Header,
        IReadOnlyList<BookChapterSummary> Catalog,
        BookReadingPosition? ReadingPosition,
        BookDetailsStatistics Statistics);

    private sealed class FakeBookManagementService : IBookDetailsQuery, IBookMetadataUpdateService, IBookDeletionService
    {
        private FakeDetailsState _details = CreateDetails();
        private TaskCompletionSource<IReadOnlyList<BookChapterSummary>>? _blockedDetailsLoadSource;
        private TaskCompletionSource<BookDetailsStatistics?>? _blockedStatisticsLoadSource;
        private BookDetailsStatistics? _blockedStatisticsResult;

        public BookMetadataUpdateRequest? LastUpdateRequest { get; private set; }

        public bool ThrowOnUpdate { get; set; }

        public FakeDetailsState? NextDetailsAfterClear { get; set; }

        public FakeDetailsState? Details { get; init; }

        public bool BlockDetailsLoad { get; set; }

        public bool BlockStatisticsLoad { get; set; }

        public int DeleteCallCount { get; private set; }

        public int GetBookDetailsHeaderCallCount { get; private set; }

        public int GetBookDetailsCallCount { get; private set; }

        public int GetBookDetailsStatisticsCallCount { get; private set; }

        public Task<BookDetailsHeader?> GetHeaderAsync(string bookId, CancellationToken cancellationToken)
        {
            GetBookDetailsHeaderCallCount++;
            var details = GetDetails();
            return Task.FromResult<BookDetailsHeader?>(details.Header);
        }

        public Task<IReadOnlyList<BookChapterSummary>> GetCatalogAsync(string bookId, CancellationToken cancellationToken)
        {
            GetBookDetailsCallCount++;
            var details = GetDetails();
            if (BlockDetailsLoad)
            {
                _blockedDetailsLoadSource = new TaskCompletionSource<IReadOnlyList<BookChapterSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => _blockedDetailsLoadSource.TrySetCanceled(cancellationToken));
                return _blockedDetailsLoadSource.Task;
            }

            return Task.FromResult(details.Catalog);
        }

        public Task<BookReadingPosition?> GetReadingPositionAsync(string bookId, CancellationToken cancellationToken)
            => Task.FromResult(GetDetails().ReadingPosition);

        public Task<BookDetailsStatistics?> GetStatisticsAsync(string bookId, CancellationToken cancellationToken)
        {
            GetBookDetailsStatisticsCallCount++;
            var details = GetDetails();
            if (BlockStatisticsLoad)
            {
                _blockedStatisticsResult = details.Statistics;
                _blockedStatisticsLoadSource = new TaskCompletionSource<BookDetailsStatistics?>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => _blockedStatisticsLoadSource.TrySetCanceled(cancellationToken));
                return _blockedStatisticsLoadSource.Task;
            }

            return Task.FromResult<BookDetailsStatistics?>(details.Statistics);
        }

        private FakeDetailsState GetDetails()
        {
            if (NextDetailsAfterClear is not null)
            {
                _details = NextDetailsAfterClear;
                NextDetailsAfterClear = null;
            }

            return Details ?? _details;
        }

        public void ReleaseBlockedDetailsLoad()
        {
            BlockDetailsLoad = false;
            _blockedDetailsLoadSource?.TrySetResult(GetDetails().Catalog);
        }

        public void ReleaseBlockedStatisticsLoad()
        {
            BlockStatisticsLoad = false;
            _blockedStatisticsLoadSource?.TrySetResult(_blockedStatisticsResult);
        }

        public Task<BookDetailsHeader> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
        {
            if (ThrowOnUpdate)
            {
                throw new InvalidOperationException("更新失败");
            }

            LastUpdateRequest = request;
            _details = _details with
            {
                Header = _details.Header with
                {
                    Title = request.Title,
                    Author = request.Author
                }
            };
            return Task.FromResult(_details.Header);
        }

        public Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
        {
            DeleteCallCount++;
            return Task.FromResult<BookDeleteResult?>(new BookDeleteResult(request.BookId, request.DeleteAudioCache, 3, true));
        }
    }

    private sealed class FakeCacheDetailsDependencies : IAudioCacheStore, ICacheCoverageQuery, ICacheInvalidationCoordinator
    {
        private EventHandler<CacheInvalidationBatch>? _batchPublished;

        public IReadOnlyList<ChapterCacheStatus> Statuses { get; set; } = [];

        public Func<IReadOnlyCollection<int>, IReadOnlyList<ChapterCacheStatus>>? StatusHandler { get; set; }

        public int StatusCallCount { get; private set; }

        public int SubscriberCount => _batchPublished?.GetInvocationList().Length ?? 0;

        public event EventHandler<CacheInvalidationBatch>? BatchPublished
        {
            add => _batchPublished += value;
            remove => _batchPublished -= value;
        }

        public AudioCacheStoreCleanupResult ClearBookResult { get; set; } = new(2048, 1, 0, 0);

        public int ClearBookCallCount { get; private set; }

        public bool BlockClearBook { get; set; }

        private TaskCompletionSource<AudioCacheStoreCleanupResult>? _blockedClearBookSource;

        public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CachedBookStoreSummary?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CachedChapterStoreSummary?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
            IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
            SynthesisProfileFingerprint synthesisProfile,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
            IReadOnlyCollection<AudioCacheKey> keys,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            StatusCallCount++;
            return Task.FromResult(StatusHandler?.Invoke(chapterIndices) ?? Statuses);
        }

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            IReadOnlyCollection<PlaybackChapterMetadata> chapters,
            CancellationToken cancellationToken) =>
            GetAsync(bookId, chapterIndices, cancellationToken);

        public Task<AudioCacheStoreCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
        {
            ClearBookCallCount++;
            if (BlockClearBook)
            {
                _blockedClearBookSource = new TaskCompletionSource<AudioCacheStoreCleanupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => _blockedClearBookSource.TrySetCanceled(cancellationToken));
                return _blockedClearBookSource.Task;
            }

            return Task.FromResult(ClearBookResult);
        }

        public void ReleaseBlockedClearBook()
        {
            BlockClearBook = false;
            _blockedClearBookSource?.TrySetResult(ClearBookResult);
        }

        public Task<AudioCacheStoreCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RunMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Publish(CacheInvalidation invalidation) =>
            _batchPublished?.Invoke(this, new CacheInvalidationBatch([invalidation]));

        public Task FlushPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed;

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public void Publish(AppSettings settings)
        {
            var previous = Current;
            Current = settings;
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, settings));
        }
    }

    private sealed class ImmediateUiScheduler : IUiScheduler
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }
    }

    private sealed class QueuedUiScheduler : IUiScheduler
    {
        private readonly Queue<(Action Action, TaskCompletionSource Completion)> _pending = [];
        private bool _isExecuting;

        public int PendingCount => _pending.Count;

        public bool QueueActions { get; set; }

        public bool CheckAccess() => !QueueActions || _isExecuting;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (!QueueActions || _isExecuting)
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue((action, completion));
            return completion.Task;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void RunNext()
        {
            var pending = _pending.Dequeue();
            _isExecuting = true;
            try
            {
                pending.Action();
                pending.Completion.TrySetResult();
            }
            finally
            {
                _isExecuting = false;
            }
        }
    }

    private sealed class QueuedPlaybackUiScheduler : IUiScheduler
    {
        private readonly Queue<Action> _pending = [];

        public bool QueueActions { get; set; }

        public bool CheckAccess() => !QueueActions;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!QueueActions)
            {
                action();
                return Task.CompletedTask;
            }

            _pending.Enqueue(action);
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        public void RunNext()
        {
            _pending.Dequeue()();
        }

        public void RunAll()
        {
            while (_pending.Count > 0)
            {
                RunNext();
            }
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public ProjectedUiError Project(Exception exception)
        {
            return new ExceptionProjector().Project(exception);
        }

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastTitle = title;
            LastMessage = projected.UserMessage;
        }

        public void ShowSuccess(string title, string message)
        {
            LastTitle = title;
            LastMessage = message;
        }

        public void ShowWarning(string title, string message)
        {
            LastTitle = title;
            LastMessage = message;
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            LastTitle = title;
            LastMessage = message;
            return Task.FromResult(AppConfirmationDecision.Confirm);
        }
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Cancel;

        public UnsavedChangesDecision NextUnsavedDecision { get; set; } = UnsavedChangesDecision.Cancel;

        public string? LastMessage { get; private set; }

        public string? LastPrimaryButtonText { get; private set; }

        public string? LastTitle { get; private set; }

        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
        {
            LastTitle = title;
            LastMessage = message;
            LastPrimaryButtonText = primaryButtonText;
            return Task.FromResult(NextConfirmationDecision);
        }

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
            string title,
            string message,
            string saveButtonText,
            string discardButtonText,
            string cancelButtonText,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NextUnsavedDecision);
        }
    }

    private sealed class FakeBookDeleteDialogService : IBookDeleteDialogService
    {
        public BookDeleteDialogResult NextResult { get; set; } = new(false, true);

        public Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakePlaybackCoordinator : IPlaybackBookCommands
    {
        public FakePlaybackCoordinator()
            : this(PlaybackSnapshot.Idle)
        {
        }

        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public string? LastRefreshedBookId { get; private set; }

        public string? LastDeletedBookId { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken)
        {
            LastRefreshedBookId = bookId;
            return Task.CompletedTask;
        }

        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken)
        {
            LastDeletedBookId = bookId;
            CurrentSnapshot = PlaybackSnapshot.Idle;
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGuardedNavigationService : IAppNavigator
    {
        public AppRoute? LastNavigationRoute { get; private set; }

        public int NavigateBackCallCount { get; private set; }

        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false)
        {
            NavigateBackCallCount++;
            return Task.FromResult(true);
        }

        public Task<bool> NavigateAsync(
            AppRoute route,
            CancellationToken cancellationToken,
            bool bypassGuard = false)
        {
            LastNavigationRoute = route;
            return Task.FromResult(true);
        }
    }
}
