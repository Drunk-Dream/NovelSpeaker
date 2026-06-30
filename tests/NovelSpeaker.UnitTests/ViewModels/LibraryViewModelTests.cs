using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Pages;
using Wpf.Ui;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task ImportFileAsync_prepares_preview_and_waits_for_confirmation()
    {
        var importService = new FakeBookImportService(
            new BookImportAnalysis(
                BookImportAnalysisStatus.ReadyToCommit,
                "C:\\books\\demo.txt",
                "demo.txt",
                "demo",
                "utf-8",
                "preview",
                "第一章 开始\n正文",
                "hash",
                [new BookImportChapter(0, 0, "第一章 开始", 6, 2)],
                null,
                null,
                "魔性沧月",
                true),
            new BookImportResult("book-1", "demo", 1));

        var catalogService = new FakeBookCatalogService([
            new BookSummary("book-1", "demo", null, "第一章 开始", DateTime.UtcNow.ToString("O"))
        ]);

        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(importService, catalogService, feedback: feedback);

        await viewModel.ImportFileAsync("C:\\books\\demo.txt", CancellationToken.None);

        Assert.Empty(viewModel.Books);
        Assert.True(viewModel.CanConfirmImport);
        Assert.True(viewModel.IsImportPanelVisible);
        Assert.True(viewModel.IsEncodingPreviewVisible);
        Assert.Equal("preview", viewModel.PreviewText);
        Assert.Equal("demo", viewModel.SuggestedTitle);
        Assert.Equal("魔性沧月", viewModel.SuggestedAuthor);
        Assert.True(viewModel.IsFileNameTemplateMatched);
        Assert.Equal(0, importService.CommitCallCount);
    }

    [Fact]
    public async Task ConfirmImportAsync_commits_pending_analysis_and_refreshes_books()
    {
        var importService = new FakeBookImportService(
            new BookImportAnalysis(
                BookImportAnalysisStatus.ReadyToCommit,
                "C:\\books\\demo.txt",
                "demo.txt",
                "demo",
                "utf-8",
                "preview",
                "第一章 开始\n正文",
                "hash",
                [new BookImportChapter(0, 0, "第一章 开始", 6, 2)],
                null,
                null,
                "魔性沧月",
                true),
            new BookImportResult("book-1", "demo", 1));

        var catalogService = new FakeBookCatalogService([
            new BookSummary("book-1", "demo", null, "第一章 开始", DateTime.UtcNow.ToString("O"))
        ]);

        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(importService, catalogService, feedback: feedback);
        await viewModel.ImportFileAsync("C:\\books\\demo.txt", CancellationToken.None);

        await viewModel.ConfirmImportCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Books);
        Assert.Equal("demo", viewModel.Books[0].Title);
        Assert.Equal(1, importService.CommitCallCount);
        Assert.False(viewModel.CanConfirmImport);
        Assert.False(viewModel.IsImportPanelVisible);
        Assert.Equal("导入成功", feedback.LastTitle);
    }

    [Fact]
    public async Task LoadAsync_maps_book_summary_to_card_fields()
    {
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService([
                new BookSummary(
                    "book-1",
                    "三体",
                    null,
                    "第一章 科学边界",
                    DateTime.UtcNow.ToString("O"),
                    LastPlayedAt: "2026-06-29T10:00:00.0000000Z",
                    TotalChapterCount: 8,
                    CurrentChapterIndex: 7,
                    RemainingChapterCount: 0,
                    OverallProgress: 1,
                    HasReadingProgress: true)
            ]));

        await viewModel.LoadAsync(CancellationToken.None);

        var book = Assert.Single(viewModel.Books);
        Assert.Equal("未知作者", book.DisplayAuthor);
        Assert.Equal("最后一章", book.RemainingChapterText);
        Assert.Equal(1d, book.ProgressRatio);
        Assert.True(book.HasReadingProgress);
        Assert.Equal("三体", book.Title);
        Assert.Equal("第一章 科学边界", book.CurrentChapterTitle);
        Assert.Equal("三体".ToUpperInvariant(), book.Cover.NormalizedTitleKey);
    }

    [Fact]
    public async Task LoadAsync_sorts_recent_reading_before_unplayed_books_by_default()
    {
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService([
                new BookSummary("book-a", "Beta", null, "章一", DateTime.UtcNow.ToString("O"), "2026-06-29T11:00:00.0000000Z", 8, 1, 6, 0.25, true),
                new BookSummary("book-b", "Alpha", null, "章一", DateTime.UtcNow.ToString("O"), "2026-06-29T12:00:00.0000000Z", 8, 2, 5, 0.375, true),
                new BookSummary("book-c", "Gamma", null, "章一", DateTime.UtcNow.ToString("O"), null, 8, null, 8, 0, false)
            ]));

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(["Alpha", "Beta", "Gamma"], viewModel.Books.Select(static book => book.Title));
    }

    [Fact]
    public async Task Selecting_title_sort_orders_books_by_normalized_title()
    {
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService([
                new BookSummary("book-2", "beta", null, "章一", DateTime.UtcNow.ToString("O")),
                new BookSummary("book-1", "Alpha", null, "章一", DateTime.UtcNow.ToString("O")),
                new BookSummary("book-3", "charlie", null, "章一", DateTime.UtcNow.ToString("O"))
            ]));

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedSortMode = LibrarySortMode.Title;

        Assert.Equal(["Alpha", "beta", "charlie"], viewModel.Books.Select(static book => book.Title));
    }

    [Fact]
    public async Task Search_filters_by_title_and_author()
    {
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService([
                new BookSummary("book-1", "球状闪电", "刘慈欣", "章一", DateTime.UtcNow.ToString("O")),
                new BookSummary("book-2", "沙丘", "Frank Herbert", "章一", DateTime.UtcNow.ToString("O"))
            ]));

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SearchText = "frank";
        await Task.Delay(180);

        var book = Assert.Single(viewModel.Books);
        Assert.Equal("沙丘", book.Title);
    }

    [Fact]
    public async Task DeleteBookAsync_keeps_current_filter_and_removes_deleted_book()
    {
        var feedback = new FakeFeedbackService
        {
            NextDecision = AppConfirmationDecision.Confirm
        };
        var managementService = new FakeBookManagementService();
        var catalogService = new FakeBookCatalogService([
            new BookSummary("book-1", "Alpha", null, "章一", DateTime.UtcNow.ToString("O")),
            new BookSummary("book-2", "Beta", null, "章一", DateTime.UtcNow.ToString("O"))
        ]);
        var viewModel = CreateViewModel(
            catalogService: catalogService,
            managementService: managementService,
            feedback: feedback);

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SearchText = "Alpha";
        await Task.Delay(180);
        catalogService.Books = [
            new BookSummary("book-2", "Beta", null, "章一", DateTime.UtcNow.ToString("O"))
        ];

        await viewModel.DeleteBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal("Alpha", viewModel.SearchText);
        Assert.Empty(viewModel.Books);
        Assert.Single(managementService.Requests);
        Assert.True(managementService.Requests[0].DeleteAudioCache);
    }

    [Fact]
    public async Task LoadAsync_keeps_search_and_sort_state()
    {
        var catalogService = new FakeBookCatalogService([
            new BookSummary("book-1", "Alpha", null, "章一", DateTime.UtcNow.ToString("O")),
            new BookSummary("book-2", "Beta", null, "章一", DateTime.UtcNow.ToString("O"))
        ]);
        var viewModel = CreateViewModel(catalogService: catalogService);

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SearchText = "Beta";
        viewModel.SelectedSortMode = LibrarySortMode.Title;
        await Task.Delay(180);
        catalogService.Books = [
            new BookSummary("book-3", "Gamma", null, "章一", DateTime.UtcNow.ToString("O")),
            new BookSummary("book-2", "Beta", null, "章一", DateTime.UtcNow.ToString("O"))
        ];

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("Beta", viewModel.SearchText);
        Assert.Equal(LibrarySortMode.Title, viewModel.SelectedSortMode);
        var book = Assert.Single(viewModel.Books);
        Assert.Equal("Beta", book.Title);
    }

    [Fact]
    public async Task Current_playing_book_cannot_be_deleted()
    {
        var playbackCoordinator = new FakePlaybackCoordinator(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Paused,
                BookId = "book-1",
                BookTitle = "Alpha"
            });
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService([
                new BookSummary("book-1", "Alpha", null, "章一", DateTime.UtcNow.ToString("O")),
                new BookSummary("book-2", "Beta", null, "章一", DateTime.UtcNow.ToString("O"))
            ]),
            playbackCoordinator: playbackCoordinator);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.Books[0].CanDelete);
        Assert.True(viewModel.Books[1].CanDelete);
    }

    [Fact]
    public async Task OpenBookCommand_navigates_to_player_page()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService([
                new BookSummary("book-1", "Alpha", null, "章一", DateTime.UtcNow.ToString("O"))
            ]),
            navigationService: navigationService);

        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.OpenBookCommand.Execute(viewModel.Books[0]);

        Assert.Equal(typeof(PlayerPage), navigationService.LastNavigateWithHierarchyPageType);
        Assert.Equal("book-1", Assert.IsType<PlayerNavigationRequest>(navigationService.LastNavigateWithHierarchyParameter).BookId);
    }

    private static LibraryViewModel CreateViewModel(
        IBookImportService? importService = null,
        FakeBookCatalogService? catalogService = null,
        FakeBookManagementService? managementService = null,
        FakeFeedbackService? feedback = null,
        FakeNavigationService? navigationService = null,
        FakePlaybackCoordinator? playbackCoordinator = null)
    {
        return new LibraryViewModel(
            importService ?? new FakeBookImportService(
                new BookImportAnalysis(
                    BookImportAnalysisStatus.ReadyToCommit,
                    "C:\\books\\demo.txt",
                    "demo.txt",
                    "demo",
                    "utf-8",
                    "preview",
                    "第一章 开始\n正文",
                    "hash",
                    [new BookImportChapter(0, 0, "第一章 开始", 6, 2)],
                    null,
                    null,
                    null,
                    false),
                new BookImportResult("book-1", "demo", 1)),
            catalogService ?? new FakeBookCatalogService([]),
            managementService ?? new FakeBookManagementService(),
            new BookCoverGenerator(),
            feedback ?? new FakeFeedbackService(),
            navigationService ?? new FakeNavigationService(),
            playbackCoordinator ?? new FakePlaybackCoordinator(PlaybackSnapshot.Idle),
            new LibraryScrollState());
    }

    private sealed class FakeBookImportService : IBookImportService
    {
        private readonly BookImportAnalysis _analysis;
        private readonly BookImportResult _result;
        public List<BookImportRequest> Requests { get; } = [];
        public int CommitCallCount { get; private set; }

        public FakeBookImportService(BookImportAnalysis analysis, BookImportResult result)
        {
            _analysis = analysis;
            _result = result;
        }

        public Task<BookImportAnalysis> AnalyzeAsync(
            BookImportRequest request,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            progress?.Report(new BookImportProgress(BookImportPhase.DetectingEncoding, 100, 100, false, "正在读取小说内容。"));
            return Task.FromResult(_analysis);
        }

        public Task<BookImportResult> CommitAsync(
            BookImportAnalysis analysis,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            CommitCallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeBookCatalogService : IBookCatalogService
    {
        public IReadOnlyList<BookSummary> Books { get; set; }

        public FakeBookCatalogService(IReadOnlyList<BookSummary> books)
        {
            Books = books;
        }

        public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Books);
        }

        public Task<ContinueListeningSummary?> GetContinueListeningAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<ContinueListeningSummary?>(null);
        }
    }

    private sealed class FakeBookManagementService : IBookManagementService
    {
        public List<BookDeleteRequest> Requests { get; } = [];

        public Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BookDetails> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<long> ClearBookCacheAsync(string bookId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<BookDeleteResult?>(new BookDeleteResult(request.BookId, request.DeleteAudioCache, 12, true));
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public AppConfirmationDecision NextDecision { get; set; } = AppConfirmationDecision.Cancel;

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

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            LastTitle = title;
            LastMessage = message;
            return Task.FromResult(NextDecision);
        }
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public Type? LastNavigateWithHierarchyPageType { get; private set; }

        public object? LastNavigateWithHierarchyParameter { get; private set; }

        public Wpf.Ui.Controls.INavigationView GetNavigationControl()
        {
            throw new NotSupportedException();
        }

        public bool GoBack() => false;

        public bool Navigate(Type pageType) => true;

        public bool Navigate(Type pageType, object? dataContext) => true;

        public bool Navigate(string pageIdOrTargetTag) => true;

        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;

        public bool NavigateWithHierarchy(Type pageType)
        {
            LastNavigateWithHierarchyPageType = pageType;
            LastNavigateWithHierarchyParameter = null;
            return true;
        }

        public bool NavigateWithHierarchy(Type pageType, object? dataContext)
        {
            LastNavigateWithHierarchyPageType = pageType;
            LastNavigateWithHierarchyParameter = dataContext;
            return true;
        }

        public void SetNavigationControl(Wpf.Ui.Controls.INavigationView navigation)
        {
        }
    }

    private sealed class FakePlaybackCoordinator : IPlaybackCoordinator
    {
        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; }

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

        public Task SkipCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
