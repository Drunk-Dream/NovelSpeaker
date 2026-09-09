using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class CacheAndDataViewModelTests
{
    [Fact]
    public async Task CommitCacheLimitAsync_blocks_values_below_minimum()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.ChangeCacheLimitUnit("MB");
        viewModel.CacheLimitValueText = "128";

        await viewModel.CommitCacheLimitAsync(CancellationToken.None);

        Assert.Equal("缓存上限不能低于 256 MB。", viewModel.CacheLimitErrorText);
    }

    [Fact]
    public async Task CommitCacheLimitAsync_canceling_lower_limit_restores_saved_value()
    {
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Cancel
        };
        var cacheStore = new FakeCacheStore
        {
            Overview = new CacheOverviewModel(3L * 1024 * 1024 * 1024, 20, AppSettings.DefaultCacheLimitBytes, true)
        };
        var viewModel = CreateViewModel(settingsService, cacheStore, dialogService: dialogService);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.CacheLimitValueText = "1";
        await viewModel.CommitCacheLimitAsync(CancellationToken.None);

        Assert.Equal(AppSettings.DefaultCacheLimitBytes, settingsService.CurrentSettings.CacheLimitBytes);
        Assert.Equal("2", viewModel.CacheLimitValueText);
        Assert.Equal("GB", viewModel.SelectedCacheLimitUnit);
    }

    [Fact]
    public async Task CommitCacheLimitAsync_confirms_trim_and_warns_when_still_over_limit()
    {
        var settingsService = new FakeAppSettingsService(AppSettings.Default with
        {
            CacheLimitBytes = 4L * 1024 * 1024 * 1024
        });
        var cacheStore = new FakeCacheStore
        {
            Overviews =
            [
                new CacheOverviewModel(3L * 1024 * 1024 * 1024, 18, 4L * 1024 * 1024 * 1024, false),
                new CacheOverviewModel(3L * 1024 * 1024 * 1024, 18, 2L * 1024 * 1024 * 1024, true)
            ]
        };
        var feedbackService = new FakeFeedbackService();
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Confirm
        };
        var viewModel = CreateViewModel(settingsService, cacheStore, dialogService, feedbackService);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.CacheLimitValueText = "2";
        await viewModel.CommitCacheLimitAsync(CancellationToken.None);

        Assert.Equal(2L * 1024 * 1024 * 1024, settingsService.CurrentSettings.CacheLimitBytes);
        Assert.True(cacheStore.TrimCalled);
        Assert.Equal("缓存仍高于上限", feedbackService.LastTitle);
    }

    [Fact]
    public async Task CommitCacheLimitAsync_waits_for_live_overview_before_deciding_to_trim()
    {
        var settingsService = new FakeAppSettingsService(AppSettings.Default with
        {
            CacheLimitBytes = 4L * 1024 * 1024 * 1024
        });
        var cacheStore = new FakeCacheStore
        {
            Overview = new CacheOverviewModel(
                1L * 1024 * 1024 * 1024,
                10,
                4L * 1024 * 1024 * 1024,
                false)
        };
        var viewModel = CreateViewModel(settingsService, cacheStore);
        await viewModel.LoadAsync(CancellationToken.None);

        var liveOverview = new TaskCompletionSource<CacheOverviewModel>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cacheStore.PendingOverviewTasks.Enqueue(liveOverview);
        var actualOverview = new CacheOverviewModel(
            3L * 1024 * 1024 * 1024,
            30,
            4L * 1024 * 1024 * 1024,
            false);
        cacheStore.Overview = actualOverview;
        cacheStore.InvalidationCoordinator.Publish(
            CacheInvalidation.ForGlobal(CacheInvalidationAspect.PhysicalSummary));
        await cacheStore.FirstOverviewLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CacheLimitValueText = "2";
        var commit = viewModel.CommitCacheLimitAsync(CancellationToken.None);
        liveOverview.SetResult(actualOverview);
        await commit;

        Assert.True(cacheStore.TrimCalled);
        Assert.Equal(2L * 1024 * 1024 * 1024, settingsService.CurrentSettings.CacheLimitBytes);
    }

    [Fact]
    public async Task CacheLimitValueText_change_debounces_and_saves_latest_value()
    {
        var timeProvider = new ManualTimeProvider();
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService, timeProvider: timeProvider);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.CacheLimitValueText = "3";
        viewModel.CacheLimitValueText = "4";

        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        await settingsService.UpdateCompleted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(4L * 1024 * 1024 * 1024, settingsService.CurrentSettings.CacheLimitBytes);
        Assert.Equal("4", viewModel.CacheLimitValueText);
    }

    [Fact]
    public async Task ClearAllAsync_is_confirmed_from_cache_and_data_page_and_refreshes_overview()
    {
        var cacheStore = new FakeCacheStore
        {
            Overviews =
            [
                new CacheOverviewModel(4096, 4, AppSettings.DefaultCacheLimitBytes, false),
                new CacheOverviewModel(1024, 1, AppSettings.DefaultCacheLimitBytes, false)
            ],
            ClearAllResult = new AudioCacheStoreCleanupResult(3072, 3, 1, 0)
        };
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Confirm
        };
        var feedbackService = new FakeFeedbackService();
        var viewModel = CreateViewModel(
            cacheStore: cacheStore,
            dialogService: dialogService,
            feedbackService: feedbackService);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ClearAllCommand.ExecuteAsync(null);

        Assert.Equal(1, cacheStore.ClearAllCallCount);
        Assert.Equal(2, cacheStore.GetOverviewCallCount);
        Assert.Equal("1 KB", viewModel.TotalCacheSizeText);
        Assert.Equal("缓存已部分清理", feedbackService.LastTitle);
    }

    [Fact]
    public async Task Active_page_tracks_physical_cache_invalidation_without_reentry()
    {
        var cacheStore = new FakeCacheStore
        {
            Overviews =
            [
                new CacheOverviewModel(4096, 4, AppSettings.DefaultCacheLimitBytes, false),
                new CacheOverviewModel(1024, 1, AppSettings.DefaultCacheLimitBytes, false)
            ]
        };
        var viewModel = CreateViewModel(cacheStore: cacheStore);

        await viewModel.LoadAsync(CancellationToken.None);
        cacheStore.InvalidationCoordinator.Publish(
            CacheInvalidation.ForChapters(
                "book-1",
                [0],
                CacheInvalidationAspect.PhysicalSummary));

        Assert.Equal("1 KB", viewModel.TotalCacheSizeText);
        Assert.Equal("1 项缓存", viewModel.CacheEntryCountText);
    }

    [Fact]
    public async Task Reentering_page_does_not_reuse_cancelled_overview_refresh()
    {
        var cacheStore = new FakeCacheStore();
        var firstOverview = new TaskCompletionSource<CacheOverviewModel>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cacheStore.PendingOverviewTasks.Enqueue(firstOverview);
        using var firstActivation = new CancellationTokenSource();
        var viewModel = CreateViewModel(cacheStore: cacheStore);

        var firstLoad = viewModel.LoadAsync(firstActivation.Token);
        await cacheStore.FirstOverviewLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Deactivate();
        firstActivation.Cancel();
        cacheStore.Overview = new CacheOverviewModel(2048, 2, AppSettings.DefaultCacheLimitBytes, false);

        await viewModel.LoadAsync(CancellationToken.None);
        await firstLoad;

        Assert.Equal(2, cacheStore.GetOverviewCallCount);
        Assert.Equal("2 KB", viewModel.TotalCacheSizeText);
    }

    private static CacheAndDataViewModel CreateViewModel(
        FakeAppSettingsService? settingsService = null,
        FakeCacheStore? cacheStore = null,
        FakeAppDialogService? dialogService = null,
        FakeFeedbackService? feedbackService = null,
        FakeDiagnosticsService? diagnosticsService = null,
        TimeProvider? timeProvider = null)
    {
        var store = cacheStore ?? new FakeCacheStore();
        return new CacheAndDataViewModel(
            settingsService ?? new FakeAppSettingsService(AppSettings.Default),
            store,
            new FakeCacheCatalog(store),
            store.InvalidationCoordinator,
            diagnosticsService ?? new FakeDiagnosticsService(),
            new FakeNavigationService(),
            dialogService ?? new FakeAppDialogService(),
            feedbackService ?? new FakeFeedbackService(),
            timeProvider,
            new InlineUiScheduler());
    }

    private sealed class FakeCacheCatalog(FakeCacheStore store) : ICacheCatalog
    {
        public async Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken)
        {
            var summary = await store.GetSummaryAsync(cancellationToken);
            return new CacheOverviewModel(
                summary.TotalSizeBytes,
                summary.EntryCount,
                summary.LimitBytes,
                summary.IsOverLimit);
        }

        public async Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(
            IReadOnlyCollection<string> bookIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<CachedBookSummary?> GetCachedBookAsync(string bookId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<CachedChapterCatalogEntry>> GetCachedChapterCatalogAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<CachedChapterSummary>> GetCachedChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<CachedChapterSummary?> GetCachedChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InlineUiScheduler : IUiScheduler
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

    private sealed class FakeCacheStore : IAudioCacheStore
    {
        private readonly Queue<CacheOverviewModel> _overviewQueue = new();

        public FakeCacheInvalidationCoordinator InvalidationCoordinator { get; } = new();

        public CacheOverviewModel Overview { get; set; } = new(0, 0, AppSettings.DefaultCacheLimitBytes, false);

        public IReadOnlyList<CacheOverviewModel>? Overviews
        {
            set
            {
                _overviewQueue.Clear();
                if (value is null)
                {
                    return;
                }

                foreach (var overview in value)
                {
                    _overviewQueue.Enqueue(overview);
                }
            }
        }

        public bool TrimCalled { get; private set; }

        public AudioCacheStoreCleanupResult ClearAllResult { get; set; } = new(0, 0, 0, 0);

        public int ClearAllCallCount { get; private set; }

        public int GetOverviewCallCount { get; private set; }

        public Queue<TaskCompletionSource<CacheOverviewModel>> PendingOverviewTasks { get; } = new();

        public TaskCompletionSource FirstOverviewLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken)
        {
            GetOverviewCallCount++;
            if (PendingOverviewTasks.Count > 0)
            {
                FirstOverviewLoadStarted.TrySetResult();
                return WaitForOverviewAsync(PendingOverviewTasks.Dequeue(), cancellationToken);
            }

            if (_overviewQueue.Count > 0)
            {
                Overview = _overviewQueue.Dequeue();
            }

            return Task.FromResult(new AudioCacheStoreSummary(
                Overview.TotalSizeBytes,
                Overview.EntryCount,
                Overview.LimitBytes,
                Overview.IsOverLimit));
        }

        private static async Task<AudioCacheStoreSummary> WaitForOverviewAsync(
            TaskCompletionSource<CacheOverviewModel> pendingOverview,
            CancellationToken cancellationToken)
        {
            var overview = await pendingOverview.Task.WaitAsync(cancellationToken);
            return new AudioCacheStoreSummary(
                overview.TotalSizeBytes,
                overview.EntryCount,
                overview.LimitBytes,
                overview.IsOverLimit);
        }

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
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
            IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
            SynthesisProfileFingerprint synthesisProfile,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
            IReadOnlyCollection<AudioCacheKey> keys,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AudioCacheStoreCleanupResult> ClearBookAsync(
            string bookId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

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

        public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
        {
            ClearAllCallCount++;
            InvalidationCoordinator.Publish(
                CacheInvalidation.ForGlobal(CacheInvalidationAspect.PhysicalSummary));
            return Task.FromResult(ClearAllResult);
        }

        public Task RunMaintenanceAsync(CancellationToken cancellationToken)
        {
            TrimCalled = true;
            InvalidationCoordinator.Publish(
                CacheInvalidation.ForGlobal(CacheInvalidationAspect.PhysicalSummary));
            return Task.CompletedTask;
        }

        public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCacheInvalidationCoordinator : ICacheInvalidationCoordinator
    {
        public event EventHandler<CacheInvalidationBatch>? BatchPublished;

        public void Publish(CacheInvalidation invalidation) =>
            BatchPublished?.Invoke(this, new CacheInvalidationBatch([invalidation]));

        public Task FlushPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public FakeAppSettingsService(AppSettings currentSettings)
        {
            CurrentSettings = currentSettings.Normalize();
        }

        public AppSettings CurrentSettings { get; private set; }
        public AppSettings Current => CurrentSettings;
        public Task UpdateCompleted => _updateCompleted.Task;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        private readonly TaskCompletionSource _updateCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            CurrentSettings = (CurrentSettings with
            {
                CacheLimitBytes = update.CacheLimitBytes ?? CurrentSettings.CacheLimitBytes
            }).Normalize();
            _updateCompleted.TrySetResult();
            return Task.FromResult(CurrentSettings);
        }
    }

    private sealed class FakeDiagnosticsService : IAppDiagnosticsService
    {
        public Task<AppDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task OpenAppDataDirectoryAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenLogsDirectoryAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> GetRedactedSummaryAsync(CancellationToken cancellationToken) => Task.FromResult("诊断摘要");

        public Task OpenThirdPartyNoticesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Confirm;

        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
        {
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
            return Task.FromResult(UnsavedChangesDecision.Cancel);
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected) => LastTitle = title;
        public void ShowSuccess(string title, string message) => LastTitle = title;
        public void ShowWarning(string title, string message) => LastTitle = title;
        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken) => Task.FromResult(AppConfirmationDecision.Cancel);
    }

    private sealed class FakeNavigationService : IAppNavigator
    {
        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(false);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(route == AppRoutes.CacheManagement);
    }
}
