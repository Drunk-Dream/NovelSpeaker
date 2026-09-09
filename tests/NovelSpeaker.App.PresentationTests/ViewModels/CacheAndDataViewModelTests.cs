using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Cache;
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
        var cacheStore = new CachePresentationTestDouble
        {
            StoreSummary = new AudioCacheStoreSummary(3L * 1024 * 1024 * 1024, 20, AppSettings.DefaultCacheLimitBytes, true)
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
        var cacheStore = new CachePresentationTestDouble
        {
            SummarySequence =
            [
                new AudioCacheStoreSummary(3L * 1024 * 1024 * 1024, 18, 4L * 1024 * 1024 * 1024, false),
                new AudioCacheStoreSummary(3L * 1024 * 1024 * 1024, 18, 2L * 1024 * 1024 * 1024, true)
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
        Assert.True(cacheStore.MaintenanceCalled);
        Assert.Equal("缓存仍高于上限", feedbackService.LastTitle);
    }

    [Fact]
    public async Task CommitCacheLimitAsync_waits_for_live_overview_before_deciding_to_trim()
    {
        var settingsService = new FakeAppSettingsService(AppSettings.Default with
        {
            CacheLimitBytes = 4L * 1024 * 1024 * 1024
        });
        var cacheStore = new CachePresentationTestDouble
        {
            StoreSummary = new AudioCacheStoreSummary(
                1L * 1024 * 1024 * 1024,
                10,
                4L * 1024 * 1024 * 1024,
                false)
        };
        var viewModel = CreateViewModel(settingsService, cacheStore);
        await viewModel.LoadAsync(CancellationToken.None);

        var liveOverview = new TaskCompletionSource<AudioCacheStoreSummary>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cacheStore.PendingSummaryTasks.Enqueue(liveOverview);
        var actualOverview = new AudioCacheStoreSummary(
            3L * 1024 * 1024 * 1024,
            30,
            4L * 1024 * 1024 * 1024,
            false);
        cacheStore.StoreSummary = actualOverview;
        cacheStore.Publish(
            CacheInvalidation.ForGlobal(CacheInvalidationAspect.PhysicalSummary));
        await cacheStore.SummaryLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CacheLimitValueText = "2";
        var commit = viewModel.CommitCacheLimitAsync(CancellationToken.None);
        liveOverview.SetResult(actualOverview);
        await commit;

        Assert.True(cacheStore.MaintenanceCalled);
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
        var cacheStore = new CachePresentationTestDouble
        {
            SummarySequence =
            [
                new AudioCacheStoreSummary(4096, 4, AppSettings.DefaultCacheLimitBytes, false),
                new AudioCacheStoreSummary(1024, 1, AppSettings.DefaultCacheLimitBytes, false)
            ],
            CleanupResult = new AudioCacheStoreCleanupResult(3072, 3, 1, 0)
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
        Assert.Equal(2, cacheStore.SummaryQueryCallCount);
        Assert.Equal("1 KB", viewModel.TotalCacheSizeText);
        Assert.Equal("缓存已部分清理", feedbackService.LastTitle);
    }

    [Fact]
    public async Task Active_page_tracks_physical_cache_invalidation_without_reentry()
    {
        var cacheStore = new CachePresentationTestDouble
        {
            SummarySequence =
            [
                new AudioCacheStoreSummary(4096, 4, AppSettings.DefaultCacheLimitBytes, false),
                new AudioCacheStoreSummary(1024, 1, AppSettings.DefaultCacheLimitBytes, false)
            ]
        };
        var viewModel = CreateViewModel(cacheStore: cacheStore);

        await viewModel.LoadAsync(CancellationToken.None);
        cacheStore.Publish(
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
        var cacheStore = new CachePresentationTestDouble();
        var firstOverview = new TaskCompletionSource<AudioCacheStoreSummary>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cacheStore.PendingSummaryTasks.Enqueue(firstOverview);
        using var firstActivation = new CancellationTokenSource();
        var viewModel = CreateViewModel(cacheStore: cacheStore);

        var firstLoad = viewModel.LoadAsync(firstActivation.Token);
        await cacheStore.SummaryLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Deactivate();
        firstActivation.Cancel();
        cacheStore.StoreSummary = new AudioCacheStoreSummary(2048, 2, AppSettings.DefaultCacheLimitBytes, false);

        await viewModel.LoadAsync(CancellationToken.None);
        await firstLoad;

        Assert.Equal(2, cacheStore.SummaryQueryCallCount);
        Assert.Equal("2 KB", viewModel.TotalCacheSizeText);
    }

    private static CacheAndDataViewModel CreateViewModel(
        FakeAppSettingsService? settingsService = null,
        CachePresentationTestDouble? cacheStore = null,
        FakeAppDialogService? dialogService = null,
        FakeFeedbackService? feedbackService = null,
        FakeDiagnosticsService? diagnosticsService = null,
        TimeProvider? timeProvider = null)
    {
        var store = cacheStore ?? new CachePresentationTestDouble();
        return new CacheAndDataViewModel(
            settingsService ?? new FakeAppSettingsService(AppSettings.Default),
            store,
            store,
            store,
            diagnosticsService ?? new FakeDiagnosticsService(),
            new FakeNavigationService(),
            dialogService ?? new FakeAppDialogService(),
            feedbackService ?? new FakeFeedbackService(),
            timeProvider,
            new InlineUiScheduler());
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
