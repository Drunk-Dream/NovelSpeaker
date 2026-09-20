using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Books.Library;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using WpfUiButton = Wpf.Ui.Controls.Button;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed partial class BookDetailsPageTests
{
    private void BookDetailsPage_scrolls_to_current_chapter_when_async_catalog_load_finishes()
    {
        WpfTestHost.RunInSta(() =>
        {
            const int chapterCount = 180;
            const int currentChapterIndex = 90;
            var managementService = new FakeBookManagementService
            {
                Header = new BookDetailsHeader("book-1", "示例小说", "作者甲"),
                Details = CreateDetails(chapterCount, currentChapterIndex)
            };
            var viewModel = CreateViewModel(managementService);
            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService())
            {
                DataContext = new BookDetailsRoute("book-1")
            };
            var frame = new Frame
            {
                NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
            };
            var window = new Window
            {
                Width = 1280,
                Height = 760,
                Content = frame
            };

            try
            {
                WpfWindowHost.Show(window);
                frame.Navigate(page);
                page.OnNavigatedToAsync().GetAwaiter().GetResult();

                var chaptersListBox = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                chaptersListBox.ApplyTemplate();
                chaptersListBox.UpdateLayout();
                var chaptersScrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                    VisualTreeTestHelper.FindDescendant<ScrollViewer>(chaptersListBox));

                WaitUntil(
                    () => chaptersScrollViewer.VerticalOffset > 0 &&
                          viewModel.CurrentChapterItem is { } currentItem &&
                          chaptersListBox.ItemContainerGenerator.ContainerFromItem(currentItem) is FrameworkElement,
                    TimeSpan.FromSeconds(2));

                var currentItem = viewModel.CurrentChapterItem;
                Assert.NotNull(currentItem);
                var currentContainer = chaptersListBox.ItemContainerGenerator.ContainerFromItem(currentItem!) as FrameworkElement;
                Assert.NotNull(currentContainer);
                var currentTop = currentContainer!.TranslatePoint(new Point(0, 0), chaptersScrollViewer).Y;

                Assert.InRange(currentTop, 0d, chaptersScrollViewer.ViewportHeight - currentContainer.ActualHeight);
            }
            finally
            {
                page.OnNavigatedFromAsync().GetAwaiter().GetResult();
                window.Close();
            }
        });
    }

    [Fact]
    public void Book_details_page_async_catalog_contract_preserves_current_chapter_scroll()
    {
        BookDetailsPage_scrolls_to_current_chapter_when_async_catalog_load_finishes();
    }

    [Fact]
    public void Book_details_player_return_route_reloads_the_requested_book()
    {
        WpfTestHost.RunInSta(() =>
        {
            var navigation = new RecordingNavigationService();
            var navigator = new ShellNavigationAdapter(new FakeNavigationGuardService(), navigation);
            var detailsRoute = new BookDetailsRoute("book-A");

            Assert.True(navigator.NavigateAsync(detailsRoute, CancellationToken.None, bypassGuard: true)
                .GetAwaiter()
                .GetResult());
            Assert.True(navigator.NavigateAsync(
                    new PlayerRoute("book-A", detailsRoute),
                    CancellationToken.None,
                    bypassGuard: true)
                .GetAwaiter()
                .GetResult());
            Assert.True(navigator.NavigateBackAsync(CancellationToken.None, bypassGuard: true)
                .GetAwaiter()
                .GetResult());

            var returnedRoute = Assert.IsType<BookDetailsRoute>(navigation.LastDataContext);
            Assert.Equal("book-A", returnedRoute.BookId);

            var managementService = new FakeBookManagementService
            {
                Header = new BookDetailsHeader("book-A", "书籍 A", "作者 A"),
                Details = CreateDetails("book-A", 1, 0) with
                {
                    Header = new BookDetailsHeader("book-A", "书籍 A", "作者 A")
                }
            };
            var viewModel = CreateViewModel(managementService);
            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService())
            {
                DataContext = returnedRoute
            };

            try
            {
                page.OnNavigatedToAsync().GetAwaiter().GetResult();
                page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                WaitUntil(() => !viewModel.IsBusy, TimeSpan.FromSeconds(2));

                Assert.Equal("book-A", managementService.LastRequestedBookId);
                Assert.Equal("书籍 A", viewModel.Title);
                Assert.Single(viewModel.Chapters);
            }
            finally
            {
                page.OnNavigatedFromAsync().GetAwaiter().GetResult();
            }
        });
    }

    private static BookDetailsViewModel CreateViewModel(FakeBookManagementService? managementService = null)
    {
        managementService ??= new FakeBookManagementService();
        return new BookDetailsViewModel(
            managementService,
            managementService,
            managementService,
            new CacheStoreTestDouble(),
            new CacheCoverageTestDouble(),
            new CacheInvalidationTestDouble(),
            new FakeAppSettingsService(),
            new BookCoverGenerator(),
            new FakeFeedbackService(),
            new FakeAppDialogService(),
            new FakeBookDeleteDialogService(),
            new BookCatalogInvalidationState(),
            new FakePlaybackCoordinator(),
            new FakeGuardedNavigationService());
    }

    private sealed record FakeDetailsState(
        BookDetailsHeader Header,
        IReadOnlyList<BookChapterSummary> Catalog,
        BookReadingPosition? ReadingPosition,
        BookDetailsStatistics Statistics);

    private static FakeDetailsState CreateDetails(int chapterCount, int currentChapterIndex)
        => CreateDetails("book-1", chapterCount, currentChapterIndex);

    private static FakeDetailsState CreateDetails(string bookId, int chapterCount, int currentChapterIndex)
    {
        return new FakeDetailsState(
            new BookDetailsHeader(bookId, "示例小说", "作者甲"),
            Enumerable.Range(0, chapterCount)
                .Select(index => new BookChapterSummary(
                    index,
                    $"第 {index + 1} 章 标题",
                    index * 100,
                    100))
                .ToArray(),
            new BookReadingPosition(bookId, currentChapterIndex, 0, 0, 0, DateTimeOffset.UtcNow),
            new BookDetailsStatistics(0));
    }

    private static void WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (predicate())
            {
                return;
            }

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        Assert.True(predicate());
    }

    private sealed class FakeNavigationGuardService : INavigationGuardService
    {
        public IDisposable Register(Func<CancellationToken, Task<bool>> guard) => new Registration();

        public Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        private sealed class Registration : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public object? LastDataContext { get; private set; }

        public int NavigationCount { get; private set; }

        public Wpf.Ui.Controls.INavigationView GetNavigationControl() => throw new NotSupportedException();

        public bool GoBack() => throw new InvalidOperationException("Application navigation must not use Wpf.Ui history.");

        public bool Navigate(Type pageType) => NavigateWithHierarchy(pageType, null);

        public bool Navigate(Type pageType, object? dataContext) => NavigateWithHierarchy(pageType, dataContext);

        public bool Navigate(string pageIdOrTargetTag) => false;

        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => false;

        public bool NavigateWithHierarchy(Type pageType) => NavigateWithHierarchy(pageType, null);

        public bool NavigateWithHierarchy(Type pageType, object? dataContext)
        {
            NavigationCount++;
            LastDataContext = dataContext;
            return true;
        }

        public void SetNavigationControl(Wpf.Ui.Controls.INavigationView navigation)
        {
        }
    }

    private sealed class FakeGuardedNavigationService : IAppNavigator
    {
        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) => Task.FromResult(true);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false)
            => Task.FromResult(true);
    }

    private sealed class FakeBookManagementService : IBookDetailsQuery, IBookMetadataUpdateService, IBookDeletionService
    {
        public BookDetailsHeader? Header { get; init; }

        public FakeDetailsState? Details { get; init; }

        public string? LastRequestedBookId { get; private set; }

        public Task<BookDetailsHeader?> GetHeaderAsync(string bookId, CancellationToken cancellationToken)
        {
            LastRequestedBookId = bookId;
            return Task.FromResult<BookDetailsHeader?>(Header ?? Details?.Header);
        }

        public Task<IReadOnlyList<BookChapterSummary>> GetCatalogAsync(string bookId, CancellationToken cancellationToken)
        {
            LastRequestedBookId = bookId;
            return Task.FromResult<IReadOnlyList<BookChapterSummary>>(Details?.Catalog ?? []);
        }

        public Task<BookReadingPosition?> GetReadingPositionAsync(string bookId, CancellationToken cancellationToken)
            => Task.FromResult(Details?.ReadingPosition);

        public Task<BookDetailsStatistics?> GetStatisticsAsync(string bookId, CancellationToken cancellationToken)
            => Task.FromResult<BookDetailsStatistics?>(Details is null ? null : Details.Statistics);

        public Task<BookDetailsHeader> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
            => Task.FromResult<BookDeleteResult?>(null);
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public AppSettings Current => AppSettings.Default;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            Task.FromResult(Current);
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);

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
            => Task.FromResult(AppConfirmationDecision.Cancel);
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
            => Task.FromResult(AppConfirmationDecision.Cancel);

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
            string title,
            string message,
            string saveButtonText,
            string discardButtonText,
            string cancelButtonText,
            CancellationToken cancellationToken)
            => Task.FromResult(UnsavedChangesDecision.Cancel);
    }

    private sealed class FakeBookDeleteDialogService : IBookDeleteDialogService
    {
        public Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new BookDeleteDialogResult(false, true));
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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
    }
}
