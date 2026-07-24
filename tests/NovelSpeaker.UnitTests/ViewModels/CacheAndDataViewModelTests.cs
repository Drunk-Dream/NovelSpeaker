using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class CacheAndDataViewModelTests
{
    [Fact]
    public async Task LoadAsync_populates_overview_and_cache_limit()
    {
        var viewModel = CreateViewModel(
            settingsService: new FakeAppSettingsService(AppSettings.Default with
            {
                CacheLimitBytes = AppSettings.DefaultCacheLimitBytes
            }),
            workspaceService: new FakeCacheWorkspaceService
            {
                Overview = new CacheOverviewModel(512L * 1024 * 1024, 12, AppSettings.DefaultCacheLimitBytes, false)
            });

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.IsOverviewLoaded);
        Assert.Equal("512 MB", viewModel.TotalCacheSizeText);
        Assert.Equal("12 项缓存", viewModel.CacheEntryCountText);
        Assert.Equal("GB", viewModel.SelectedCacheLimitUnit);
        Assert.Equal("2", viewModel.CacheLimitValueText);
    }

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
    public async Task CacheLimitValueText_change_debounces_and_saves_latest_value()
    {
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.CacheLimitValueText = "3";
        viewModel.CacheLimitValueText = "4";

        await Task.Delay(700);

        Assert.Equal(4L * 1024 * 1024 * 1024, settingsService.CurrentSettings.CacheLimitBytes);
        Assert.Equal("4", viewModel.CacheLimitValueText);
    }

    private static CacheAndDataViewModel CreateViewModel(
        FakeAppSettingsService? settingsService = null,
        FakeCacheWorkspaceService? workspaceService = null,
        FakeAppDialogService? dialogService = null,
        FakeFeedbackService? feedbackService = null,
        FakeDiagnosticsService? diagnosticsService = null)
    {
        return new CacheAndDataViewModel(
            settingsService ?? new FakeAppSettingsService(AppSettings.Default),
            workspaceService ?? new FakeCacheWorkspaceService(),
            diagnosticsService ?? new FakeDiagnosticsService(),
            new FakeNavigationService(),
            dialogService ?? new FakeAppDialogService(),
            feedbackService ?? new FakeFeedbackService());
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        private readonly Queue<CacheOverviewModel> _overviewQueue = new();

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

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken)
        {
            if (_overviewQueue.Count > 0)
            {
                Overview = _overviewQueue.Dequeue();
            }

            return Task.FromResult(Overview);
        }

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken)
        {
            TrimCalled = true;
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

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            CurrentSettings = (CurrentSettings with
            {
                CacheLimitBytes = update.CacheLimitBytes ?? CurrentSettings.CacheLimitBytes
            }).Normalize();
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

    private sealed class FakeNavigationService : ITestNavigationService
    {
        public INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => false;
        public bool Navigate(Type pageType) => true;
        public bool Navigate(Type pageType, object? dataContext) => true;
        public bool Navigate(string pageIdOrTargetTag) => true;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;
        public bool NavigateWithHierarchy(Type pageType) => pageType == typeof(CacheManagementPage);
        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => pageType == typeof(CacheManagementPage);
        public void SetNavigationControl(INavigationView navigation) { }
    }
}
