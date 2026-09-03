using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Navigation;

[Collection("WpfDispatcher")]
public sealed class AppNavigationPageProviderTests
{
    [Fact]
    public void GetPage_resolves_registered_page()
    {
        WpfTestHost.RunInSta(() =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<FakeNavigationService>();
            services.AddSingleton<INavigationService>(provider => provider.GetRequiredService<FakeNavigationService>());
            services.AddSingleton<IAppNavigator>(provider => provider.GetRequiredService<FakeNavigationService>());
            services.AddSingleton<INavigationGuardService, FakeNavigationGuardService>();
            services.AddSingleton<FakeBookManagementService>();
            services.AddSingleton<IBookLibraryQuery>(provider => provider.GetRequiredService<FakeBookManagementService>());
            services.AddSingleton<IBookMetadataUpdateService>(provider => provider.GetRequiredService<FakeBookManagementService>());
            services.AddSingleton<IBookDeletionService>(provider => provider.GetRequiredService<FakeBookManagementService>());
            services.AddSingleton<ICacheWorkspaceService, FakeCacheWorkspaceService>();
            services.AddSingleton<IAppSettingsService, FakeAppSettingsService>();
            services.AddSingleton<IBookCoverGenerator, BookCoverGenerator>();
            services.AddSingleton<IAppFeedbackService, FakeAppFeedbackService>();
            services.AddSingleton<IAppDialogService, FakeAppDialogService>();
            services.AddSingleton<IBookDeleteDialogService, FakeBookDeleteDialogService>();
            services.AddSingleton<IBookCatalogInvalidationState, BookCatalogInvalidationState>();
            services.AddSingleton<IPlaybackBookCommands, FakePlaybackCoordinator>();
            services.AddTransient<BookDetailsViewModel>();
            services.AddTransient<BookDetailsPage>();

            var provider = services.BuildServiceProvider();
            try
            {
                var pageProvider = new AppNavigationPageProvider(provider);

                var page = pageProvider.GetPage(typeof(BookDetailsPage));

                Assert.IsType<BookDetailsPage>(page);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void GetPage_throws_for_unregistered_page()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var pageProvider = new AppNavigationPageProvider(provider);

        Assert.Throws<InvalidOperationException>(() => pageProvider.GetPage(typeof(UnregisteredPage)));
    }

    private sealed class UnregisteredPage : System.Windows.Controls.Page
    {
    }

    private sealed class FakeNavigationService : INavigationService, IAppNavigator
    {
        public INavigationView GetNavigationControl()
        {
            throw new NotSupportedException();
        }

        public bool GoBack()
        {
            return false;
        }

        public bool Navigate(Type pageType) => true;

        public bool Navigate(Type pageType, object? dataContext) => true;

        public bool Navigate(string pageIdOrTargetTag) => true;

        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;

        public bool NavigateWithHierarchy(Type pageType) => true;

        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;

        public void SetNavigationControl(INavigationView navigation)
        {
        }

        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) => Task.FromResult(false);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false)
            => Task.FromResult(true);
    }

    private sealed class FakeNavigationGuardService : INavigationGuardService
    {
        public IDisposable Register(Func<CancellationToken, Task<bool>> guard)
        {
            return new Registration();
        }

        public Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        private sealed class Registration : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeBookManagementService : IBookLibraryQuery, IBookMetadataUpdateService, IBookDeletionService
    {
        public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BookSummary>>([]);

        public Task<BookDetailsHeader?> GetBookDetailsHeaderAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult<BookDetailsHeader?>(null);
        }

        public Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult<BookDetails?>(null);
        }

        public Task<BookDetailsHeader> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeAppFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception) => new("error", UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Cancel);
        }
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        public event EventHandler<CacheChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CacheCleanupResult> ClearChaptersAsync(string bookId, IReadOnlyCollection<int> chapterIndices, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public AppSettings Current => AppSettings.Default;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(
            AppSettingsUpdate update,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Cancel);
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

    private sealed class FakeBookDeleteDialogService : IBookDeleteDialogService
    {
        public Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new BookDeleteDialogResult(false, true));
        }
    }

    private sealed class FakePlaybackCoordinator : IPlaybackBookCommands
    {
        public PlaybackSnapshot CurrentSnapshot { get; } = PlaybackSnapshot.Idle;

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged
        {
            add
            {
            }
            remove
            {
            }
        }

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
        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
