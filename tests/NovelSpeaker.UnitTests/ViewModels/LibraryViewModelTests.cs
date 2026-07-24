using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task LoadAsync_maps_book_summary_to_card_fields()
    {
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService(
                [
                    new BookSummary(
                        "book-1",
                        "三体",
                        null,
                        "第一章 科学边界",
                        DateTimeOffset.UtcNow,
                        LastPlayedAt: DateTimeOffset.Parse("2026-06-29T10:00:00.0000000Z"),
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
    public async Task Search_filters_books_by_title_and_author()
    {
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService(
                [
                    new BookSummary("book-1", "球状闪电", "刘慈欣", "章一", DateTimeOffset.UtcNow),
                    new BookSummary("book-2", "沙丘", "Frank Herbert", "章一", DateTimeOffset.UtcNow)
                ]));

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SearchText = "frank";
        await Task.Delay(180);

        var book = Assert.Single(viewModel.Books);
        Assert.Equal("沙丘", book.Title);
    }

    [Fact]
    public async Task LoadAsync_sorts_recent_reading_before_unplayed_books_by_default()
    {
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService(
                [
                    new BookSummary("book-a", "Beta", null, "章一", DateTimeOffset.UtcNow, DateTimeOffset.Parse("2026-06-29T11:00:00.0000000Z"), 8, 1, 6, 0.25, true),
                    new BookSummary("book-b", "Alpha", null, "章一", DateTimeOffset.UtcNow, DateTimeOffset.Parse("2026-06-29T12:00:00.0000000Z"), 8, 2, 5, 0.375, true),
                    new BookSummary("book-c", "Gamma", null, "章一", DateTimeOffset.UtcNow, null, 8, null, 8, 0, false)
                ]));

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(["Alpha", "Beta", "Gamma"], viewModel.Books.Select(static book => book.Title));
    }

    [Fact]
    public async Task Selecting_title_sort_orders_books_by_normalized_title()
    {
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService(
                [
                    new BookSummary("book-2", "beta", null, "章一", DateTimeOffset.UtcNow),
                    new BookSummary("book-1", "Alpha", null, "章一", DateTimeOffset.UtcNow),
                    new BookSummary("book-3", "charlie", null, "章一", DateTimeOffset.UtcNow)
                ]));

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedSortMode = LibrarySortMode.Title;

        Assert.Equal(["Alpha", "beta", "charlie"], viewModel.Books.Select(static book => book.Title));
    }

    [Fact]
    public async Task DeleteBookAsync_keeps_current_filter_and_removes_deleted_book()
    {
        var deleteDialogService = new FakeBookDeleteDialogService
        {
            NextResult = new BookDeleteDialogResult(true, true)
        };
        var managementService = new FakeBookManagementService();
        var catalogService = new FakeBookCatalogService(
            [
                new BookSummary("book-1", "Alpha", null, "章一", DateTimeOffset.UtcNow),
                new BookSummary("book-2", "Beta", null, "章一", DateTimeOffset.UtcNow)
            ]);
        var viewModel = CreateViewModel(
            catalogService: catalogService,
            managementService: managementService,
            deleteDialogService: deleteDialogService);

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SearchText = "Alpha";
        await Task.Delay(180);
        catalogService.Books = [new BookSummary("book-2", "Beta", null, "章一", DateTimeOffset.UtcNow)];

        await viewModel.DeleteBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal("Alpha", viewModel.SearchText);
        Assert.Empty(viewModel.Books);
        Assert.Single(managementService.Requests);
        Assert.True(managementService.Requests[0].DeleteAudioCache);
        Assert.Equal("Alpha", deleteDialogService.Requests[0].BookTitle);
    }

    [Fact]
    public async Task LoadAsync_keeps_search_and_sort_state()
    {
        var catalogService = new FakeBookCatalogService(
            [
                new BookSummary("book-1", "Alpha", null, "章一", DateTimeOffset.UtcNow),
                new BookSummary("book-2", "Beta", null, "章一", DateTimeOffset.UtcNow)
            ]);
        var viewModel = CreateViewModel(catalogService: catalogService);

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SearchText = "Beta";
        viewModel.SelectedSortMode = LibrarySortMode.Title;
        await Task.Delay(180);
        catalogService.Books =
        [
            new BookSummary("book-3", "Gamma", null, "章一", DateTimeOffset.UtcNow),
            new BookSummary("book-2", "Beta", null, "章一", DateTimeOffset.UtcNow)
        ];

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("Beta", viewModel.SearchText);
        Assert.Equal(LibrarySortMode.Title, viewModel.SelectedSortMode);
        var book = Assert.Single(viewModel.Books);
        Assert.Equal("Beta", book.Title);
    }

    [Fact]
    public async Task DeleteBookAsync_allows_current_playing_book_and_stops_playback_first()
    {
        var playbackCoordinator = new FakePlaybackCoordinator(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Paused,
                BookId = "book-1",
                BookTitle = "Alpha"
            });
        var deleteDialogService = new FakeBookDeleteDialogService
        {
            NextResult = new BookDeleteDialogResult(true, false)
        };
        var managementService = new FakeBookManagementService();
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService(
                [
                    new BookSummary("book-1", "Alpha", null, "章一", DateTimeOffset.UtcNow),
                    new BookSummary("book-2", "Beta", null, "章一", DateTimeOffset.UtcNow)
                ]),
            managementService: managementService,
            playbackCoordinator: playbackCoordinator,
            deleteDialogService: deleteDialogService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.DeleteBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal("book-1", playbackCoordinator.LastHandledDeletedBookId);
        Assert.False(managementService.Requests[0].DeleteAudioCache);
    }

    [Fact]
    public async Task OpenBookCommand_navigates_to_player_page_in_open_paused_mode()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService(
                [new BookSummary("book-1", "Alpha", null, "章一", DateTimeOffset.UtcNow)]),
            navigationService: navigationService);

        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.OpenBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal(typeof(PlayerPage), navigationService.LastNavigateWithHierarchyPageType);
        var request = Assert.IsType<PlayerNavigationRequest>(navigationService.LastNavigateWithHierarchyParameter);
        Assert.Equal("book-1", request.BookId);
        Assert.Equal(PlayerNavigationMode.OpenPaused, request.Mode);
    }

    [Fact]
    public async Task OpenBookDetailsCommand_navigates_to_book_details_page_with_book_id()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = CreateViewModel(
            catalogService: new FakeBookCatalogService(
                [new BookSummary("book-9", "Delta", null, "章一", DateTimeOffset.UtcNow)]),
            navigationService: navigationService);

        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.OpenBookDetailsCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal(typeof(BookDetailsPage), navigationService.LastNavigateWithHierarchyPageType);
        var request = Assert.IsType<BookDetailsNavigationRequest>(navigationService.LastNavigateWithHierarchyParameter);
        Assert.Equal("book-9", request.BookId);
    }

    [Fact]
    public async Task ImportFilesAsync_refreshes_books_when_import_coordinator_reports_imported()
    {
        var feedback = new FakeFeedbackService();
        var importCoordinator = new FakeLibraryImportCoordinator
        {
            NextResult = new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Imported)
        };
        var catalogService = new FakeBookCatalogService([]);
        var viewModel = CreateViewModel(
            catalogService: catalogService,
            feedback: feedback,
            importCoordinator: importCoordinator);

        catalogService.Books = [new BookSummary("book-1", "Alpha", null, "章一", DateTimeOffset.UtcNow)];
        await viewModel.ImportFilesAsync([CreateTempTxtFile()], CancellationToken.None);

        Assert.Equal("导入成功", feedback.LastTitle);
        Assert.Single(viewModel.Books);
        Assert.Single(importCoordinator.Requests);
    }

    [Fact]
    public async Task ImportFilesAsync_shows_duplicate_warning_without_refreshing_books()
    {
        var feedback = new FakeFeedbackService();
        var importCoordinator = new FakeLibraryImportCoordinator
        {
            NextResult = new LibraryImportCoordinatorResult(
                LibraryImportCoordinatorStatus.Failed,
                BookImportFailureReason.DuplicateBook)
        };
        var catalogService = new FakeBookCatalogService([]);
        var viewModel = CreateViewModel(
            catalogService: catalogService,
            feedback: feedback,
            importCoordinator: importCoordinator);

        await viewModel.ImportFilesAsync([CreateTempTxtFile()], CancellationToken.None);

        Assert.Equal("无法导入", feedback.LastTitle);
        Assert.Equal("该小说已经导入", feedback.LastMessage);
        Assert.Empty(viewModel.Books);
    }

    [Fact]
    public async Task ImportFilesAsync_cancels_previous_inflight_import_when_new_request_starts()
    {
        var firstResult = new TaskCompletionSource<LibraryImportCoordinatorResult>();
        var importCoordinator = new FakeLibraryImportCoordinator();
        importCoordinator.PendingResults.Enqueue(firstResult.Task);
        importCoordinator.PendingResults.Enqueue(Task.FromResult(new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Imported)));

        var catalogService = new FakeBookCatalogService([]);
        var viewModel = CreateViewModel(
            catalogService: catalogService,
            importCoordinator: importCoordinator);

        var firstFile = CreateTempTxtFile();
        var secondFile = CreateTempTxtFile();
        var firstImportTask = viewModel.ImportFilesAsync([firstFile], CancellationToken.None);
        await WaitForAsync(() => importCoordinator.Requests.Count == 1);

        catalogService.Books = [new BookSummary("book-1", "Alpha", null, "章一", DateTimeOffset.UtcNow)];
        var secondImportTask = viewModel.ImportFilesAsync([secondFile], CancellationToken.None);
        await WaitForAsync(() => importCoordinator.Requests.Count == 2);

        Assert.True(importCoordinator.RequestTokens[0].IsCancellationRequested);

        firstResult.SetResult(new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Cancelled));
        await firstImportTask;
        await secondImportTask;

        Assert.Single(viewModel.Books);
    }

    public static IEnumerable<object[]> InvalidImportInputs()
    {
        yield return [Array.Empty<string>(), "未检测到可导入的 TXT 文件。"];
        yield return [new[] { "one.txt", "two.txt" }, "一次只能导入一个 TXT 文件。"];
        yield return [new[] { "novel.epub" }, "只支持导入单个 .txt 文件。"];
    }

    [Theory]
    [MemberData(nameof(InvalidImportInputs))]
    public async Task ImportFilesAsync_rejects_invalid_inputs(string[] inputs, string expectedMessage)
    {
        var feedback = new FakeFeedbackService();
        var importCoordinator = new FakeLibraryImportCoordinator();
        var viewModel = CreateViewModel(feedback: feedback, importCoordinator: importCoordinator);

        await viewModel.ImportFilesAsync(inputs, CancellationToken.None);

        Assert.Equal("无法导入", feedback.LastTitle);
        Assert.Equal(expectedMessage, feedback.LastMessage);
        Assert.Empty(importCoordinator.Requests);
    }

    private static LibraryViewModel CreateViewModel(
        FakeBookCatalogService? catalogService = null,
        FakeBookManagementService? managementService = null,
        FakeLibraryImportCoordinator? importCoordinator = null,
        FakeBookDeleteDialogService? deleteDialogService = null,
        FakeFeedbackService? feedback = null,
        FakeNavigationService? navigationService = null,
        FakePlaybackCoordinator? playbackCoordinator = null)
    {
        return new LibraryViewModel(
            catalogService ?? new FakeBookCatalogService([]),
            managementService ?? new FakeBookManagementService(),
            new BookCoverGenerator(),
            importCoordinator ?? new FakeLibraryImportCoordinator(),
            deleteDialogService ?? new FakeBookDeleteDialogService(),
            new BookCatalogInvalidationState(),
            feedback ?? new FakeFeedbackService(),
            navigationService ?? new FakeNavigationService(),
            playbackCoordinator ?? new FakePlaybackCoordinator(PlaybackSnapshot.Idle),
            new LibraryScrollState());
    }

    private static string CreateTempTxtFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(filePath, "demo");
        return filePath;
    }

    private sealed class FakeBookCatalogService : IBookLibraryQuery
    {
        public FakeBookCatalogService(IReadOnlyList<BookSummary> books)
        {
            Books = books;
        }

        public IReadOnlyList<BookSummary> Books { get; set; }

        public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Books);
        }

        public Task<BookDetailsHeader?> GetBookDetailsHeaderAsync(string bookId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeLibraryImportCoordinator : ILibraryImportCoordinator
    {
        public List<string> Requests { get; } = [];
        public List<CancellationToken> RequestTokens { get; } = [];
        public Queue<Task<LibraryImportCoordinatorResult>> PendingResults { get; } = new();

        public LibraryImportCoordinatorResult NextResult { get; set; } =
            new(LibraryImportCoordinatorStatus.Cancelled);

        public Task<LibraryImportCoordinatorResult> ImportAsync(
            string filePath,
            IProgress<BookImportProgress>? inlineProgress,
            CancellationToken cancellationToken)
        {
            Requests.Add(filePath);
            RequestTokens.Add(cancellationToken);
            return PendingResults.Count > 0
                ? PendingResults.Dequeue()
                : Task.FromResult(NextResult);
        }
    }

    private sealed class FakeBookDeleteDialogService : IBookDeleteDialogService
    {
        public List<BookDeleteDialogRequest> Requests { get; } = [];

        public BookDeleteDialogResult NextResult { get; set; } = new(false, true);

        public Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeBookManagementService : IBookDeletionService
    {
        public List<BookDeleteRequest> Requests { get; } = [];

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

        public void ShowWarning(string title, string message)
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

    private sealed class FakeNavigationService : IAppNavigator
    {
        public Type? LastNavigateWithHierarchyPageType { get; private set; }

        public object? LastNavigateWithHierarchyParameter { get; private set; }

        public Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) => Task.FromResult(false);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false)
        {
            LastNavigateWithHierarchyPageType = route.Id switch
            {
                AppRouteId.Player => typeof(PlayerPage),
                AppRouteId.BookDetails => typeof(BookDetailsPage),
                _ => null
            };
            LastNavigateWithHierarchyParameter = route;
            return Task.FromResult(true);
        }
    }

    private sealed class FakePlaybackCoordinator : IPlaybackBookCommands
    {
        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public string? LastHandledDeletedBookId { get; private set; }

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

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken)
        {
            LastHandledDeletedBookId = bookId;
            CurrentSnapshot = PlaybackSnapshot.Idle;
            return Task.CompletedTask;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - startedAt > TimeSpan.FromSeconds(1))
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10);
        }
    }
}
