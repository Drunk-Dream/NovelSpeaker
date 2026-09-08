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
        var workspaceService = new FakeCacheWorkspaceService
        {
            Overview = new CacheOverviewModel(3L * 1024 * 1024 * 1024, 20, AppSettings.DefaultCacheLimitBytes, true)
        };
        var viewModel = CreateViewModel(settingsService, workspaceService, dialogService: dialogService);
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
        var workspaceService = new FakeCacheWorkspaceService
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
        var viewModel = CreateViewModel(settingsService, workspaceService, dialogService, feedbackService);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.CacheLimitValueText = "2";
        await viewModel.CommitCacheLimitAsync(CancellationToken.None);

        Assert.Equal(2L * 1024 * 1024 * 1024, settingsService.CurrentSettings.CacheLimitBytes);
        Assert.True(workspaceService.TrimCalled);
        Assert.Equal("缓存仍高于上限", feedbackService.LastTitle);
    }

    [Fact]
    public async Task CommitCacheLimitAsync_waits_for_live_overview_before_deciding_to_trim()
    {
        var settingsService = new FakeAppSettingsService(AppSettings.Default with
        {
            CacheLimitBytes = 4L * 1024 * 1024 * 1024
        });
        var workspaceService = new FakeCacheWorkspaceService
        {
            Overview = new CacheOverviewModel(
                1L * 1024 * 1024 * 1024,
                10,
                4L * 1024 * 1024 * 1024,
                false)
        };
        var viewModel = CreateViewModel(settingsService, workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);

        var liveOverview = new TaskCompletionSource<CacheOverviewModel>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceService.PendingOverviewTasks.Enqueue(liveOverview);
        var actualOverview = new CacheOverviewModel(
            3L * 1024 * 1024 * 1024,
            30,
            4L * 1024 * 1024 * 1024,
            false);
        workspaceService.Overview = actualOverview;
        workspaceService.InvalidationCoordinator.Publish(
            CacheInvalidation.ForGlobal(CacheInvalidationAspect.PhysicalSummary));
        await workspaceService.FirstOverviewLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CacheLimitValueText = "2";
        var commit = viewModel.CommitCacheLimitAsync(CancellationToken.None);
        liveOverview.SetResult(actualOverview);
        await commit;

        Assert.True(workspaceService.TrimCalled);
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
        var workspaceService = new FakeCacheWorkspaceService
        {
            Overviews =
            [
                new CacheOverviewModel(4096, 4, AppSettings.DefaultCacheLimitBytes, false),
                new CacheOverviewModel(1024, 1, AppSettings.DefaultCacheLimitBytes, false)
            ],
            ClearAllResult = new CacheCleanupResult(3072, 3, 1, 0)
        };
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Confirm
        };
        var feedbackService = new FakeFeedbackService();
        var viewModel = CreateViewModel(
            workspaceService: workspaceService,
            dialogService: dialogService,
            feedbackService: feedbackService);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ClearAllCommand.ExecuteAsync(null);

        Assert.Equal(1, workspaceService.ClearAllCallCount);
        Assert.Equal(2, workspaceService.GetOverviewCallCount);
        Assert.Equal("1 KB", viewModel.TotalCacheSizeText);
        Assert.Equal("缓存已部分清理", feedbackService.LastTitle);
    }

    [Fact]
    public async Task Active_page_tracks_physical_cache_invalidation_without_reentry()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            Overviews =
            [
                new CacheOverviewModel(4096, 4, AppSettings.DefaultCacheLimitBytes, false),
                new CacheOverviewModel(1024, 1, AppSettings.DefaultCacheLimitBytes, false)
            ]
        };
        var viewModel = CreateViewModel(workspaceService: workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);
        workspaceService.InvalidationCoordinator.Publish(
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
        var workspaceService = new FakeCacheWorkspaceService();
        var firstOverview = new TaskCompletionSource<CacheOverviewModel>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceService.PendingOverviewTasks.Enqueue(firstOverview);
        using var firstActivation = new CancellationTokenSource();
        var viewModel = CreateViewModel(workspaceService: workspaceService);

        var firstLoad = viewModel.LoadAsync(firstActivation.Token);
        await workspaceService.FirstOverviewLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Deactivate();
        firstActivation.Cancel();
        workspaceService.Overview = new CacheOverviewModel(2048, 2, AppSettings.DefaultCacheLimitBytes, false);

        await viewModel.LoadAsync(CancellationToken.None);
        await firstLoad;

        Assert.Equal(2, workspaceService.GetOverviewCallCount);
        Assert.Equal("2 KB", viewModel.TotalCacheSizeText);
    }

    private static CacheAndDataViewModel CreateViewModel(
        FakeAppSettingsService? settingsService = null,
        FakeCacheWorkspaceService? workspaceService = null,
        FakeAppDialogService? dialogService = null,
        FakeFeedbackService? feedbackService = null,
        FakeDiagnosticsService? diagnosticsService = null,
        TimeProvider? timeProvider = null)
    {
        var workspace = workspaceService ?? new FakeCacheWorkspaceService();
        return new CacheAndDataViewModel(
            settingsService ?? new FakeAppSettingsService(AppSettings.Default),
            workspace,
            new FakeCacheCatalog(workspace),
            workspace.InvalidationCoordinator,
            diagnosticsService ?? new FakeDiagnosticsService(),
            new FakeNavigationService(),
            dialogService ?? new FakeAppDialogService(),
            feedbackService ?? new FakeFeedbackService(),
            timeProvider,
            new InlineUiScheduler());
    }

    private sealed class FakeCacheCatalog(FakeCacheWorkspaceService workspace) : ICacheCatalog
    {
        public async Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) =>
            await workspace.GetOverviewAsync(cancellationToken);

        public async Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(CancellationToken cancellationToken) =>
            (await workspace.GetCachedBooksAsync(cancellationToken))
                .Select(book => new CachedBookSummary(
                    book.BookId,
                    book.Title,
                    book.Author,
                    book.ChapterCount,
                    book.EntryCount,
                    book.TotalSizeBytes))
                .ToArray();

        public async Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(
            IReadOnlyCollection<string> bookIds,
            CancellationToken cancellationToken) =>
            (await GetCachedBooksAsync(cancellationToken))
                .Where(book => bookIds.Contains(book.BookId, StringComparer.Ordinal))
                .ToArray();

        public async Task<CachedBookSummary?> GetCachedBookAsync(string bookId, CancellationToken cancellationToken) =>
            (await workspace.GetCachedBookAsync(bookId, cancellationToken)) is { } book
                ? new CachedBookSummary(book.BookId, book.Title, book.Author, book.ChapterCount, book.EntryCount, book.TotalSizeBytes)
                : null;

        public async Task<IReadOnlyList<CachedChapterCatalogEntry>> GetCachedChapterCatalogAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            (await workspace.GetCachedChaptersAsync(bookId, cancellationToken))
                .Select(chapter => new CachedChapterCatalogEntry(chapter.BookId, chapter.ChapterIndex, chapter.Title))
                .ToArray();

        public async Task<IReadOnlyList<CachedChapterSummary>> GetCachedChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            (await workspace.GetCachedChaptersAsync(bookId, cancellationToken))
                .Where(chapter => chapterIndices.Contains(chapter.ChapterIndex))
                .Select(chapter => new CachedChapterSummary(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    chapter.Title,
                    chapter.CachedSegmentCount,
                    chapter.EntryCount,
                    chapter.TotalSizeBytes))
                .ToArray();

        public async Task<CachedChapterSummary?> GetCachedChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            (await workspace.GetCachedChapterAsync(bookId, chapterIndex, cancellationToken)) is { } chapter
                ? new CachedChapterSummary(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    chapter.Title,
                    chapter.CachedSegmentCount,
                    chapter.EntryCount,
                    chapter.TotalSizeBytes)
                : null;
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

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        private readonly Queue<CacheOverviewModel> _overviewQueue = new();

        public FakeCacheInvalidationCoordinator InvalidationCoordinator { get; } = new();

        public event EventHandler<CacheChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

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

        public CacheCleanupResult ClearAllResult { get; set; } = new(0, 0, 0, 0);

        public int ClearAllCallCount { get; private set; }

        public int GetOverviewCallCount { get; private set; }

        public Queue<TaskCompletionSource<CacheOverviewModel>> PendingOverviewTasks { get; } = new();

        public TaskCompletionSource FirstOverviewLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken)
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

            return Task.FromResult(Overview);
        }

        private static async Task<CacheOverviewModel> WaitForOverviewAsync(
            TaskCompletionSource<CacheOverviewModel> pendingOverview,
            CancellationToken cancellationToken) =>
            await pendingOverview.Task.WaitAsync(cancellationToken);

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CachedBookCacheItem?> GetCachedBookAsync(string bookId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CachedChapterCacheItem?> GetCachedChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken)
        {
            TrimCalled = true;
            InvalidationCoordinator.Publish(
                CacheInvalidation.ForGlobal(CacheInvalidationAspect.PhysicalSummary));
            return Task.CompletedTask;
        }

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CacheCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
        {
            ClearAllCallCount++;
            InvalidationCoordinator.Publish(
                CacheInvalidation.ForGlobal(CacheInvalidationAspect.PhysicalSummary));
            return Task.FromResult(ClearAllResult);
        }
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
