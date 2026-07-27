using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Selection;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests;

[Collection("WpfDispatcher")]
public sealed class CacheManagementViewModelTests
{
    [Fact]
    public async Task LoadAsync_does_not_auto_select_first_book()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult =
            [
                new CachedBookCacheItem("book-1", "第一本", "作者甲", 2, 3, 1024),
                new CachedBookCacheItem("book-2", "第二本", "作者乙", 1, 1, 2048)
            ]
        };
        var viewModel = CreateViewModel(workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.HasSelection);
        Assert.True(viewModel.ShowSelectionPrompt);
        Assert.Equal(2, viewModel.Books.Count);
        Assert.DoesNotContain(viewModel.Books, static book => book.IsSelected);
    }

    [Fact]
    public async Task SelectBookAsync_ignores_late_results_from_previous_selection()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult =
            [
                new CachedBookCacheItem("book-1", "第一本", "作者甲", 2, 3, 1024),
                new CachedBookCacheItem("book-2", "第二本", "作者乙", 1, 1, 2048)
            ]
        };
        workspaceService.PendingChapterTasks["book-1"] = new TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceService.PendingChapterTasks["book-2"] = new TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);

        var firstSelection = viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var secondSelection = viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[1]);
        workspaceService.PendingChapterTasks["book-2"].SetResult(
        [
            new CachedChapterCacheItem("book-2", 0, "第二本 第一章", 1, 1, 2048, 1)
        ]);
        await secondSelection;

        Assert.Equal("第二本", viewModel.SelectedBookTitle);
        Assert.Single(viewModel.Chapters);
        Assert.Equal("第二本 第一章", viewModel.Chapters[0].Title);

        workspaceService.PendingChapterTasks["book-1"].SetResult(
        [
            new CachedChapterCacheItem("book-1", 0, "第一本 第一章", 1, 1, 1024, 1)
        ]);
        await firstSelection;

        Assert.Equal("第二本", viewModel.SelectedBookTitle);
        Assert.Single(viewModel.Chapters);
        Assert.Equal("第二本 第一章", viewModel.Chapters[0].Title);
    }

    [Fact]
    public async Task Chapter_selection_uses_desktop_modifiers_select_all_and_clear()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult =
            [
                new CachedBookCacheItem("book-1", "第一本", "作者甲", 4, 4, 4096)
            ]
        };
        workspaceService.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 1, "第二章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 2, "第三章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 3, "第四章", 1, 1, 1024, 1)
        ];
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        Assert.Equal("当前配置完整度：1/1 段 · 100%", viewModel.Chapters[0].CompletenessText);

        viewModel.HandleChapterClick(viewModel.Chapters[1], DesktopSelectionModifiers.None);
        viewModel.HandleChapterClick(viewModel.Chapters[3], DesktopSelectionModifiers.Shift);

        Assert.Equal([1, 2, 3], viewModel.SelectedChapterIndices);
        Assert.True(viewModel.CanClearSelectedChapters);
        Assert.False(viewModel.CanExportSelectedChapters);
        Assert.Equal("已选择 3 章", viewModel.ChapterSelectionSummary);
        Assert.All(viewModel.Chapters.Skip(1), chapter => Assert.True(chapter.IsSelected));

        Assert.True(viewModel.HandleSelectAllChapters());
        Assert.Equal([0, 1, 2, 3], viewModel.SelectedChapterIndices);
        Assert.True(viewModel.HandleClearChapterSelection());
        Assert.Empty(viewModel.SelectedChapterIndices);
        Assert.False(viewModel.CanClearSelectedChapters);
        Assert.False(viewModel.ClearSelectedChaptersCommand.CanExecute(null));
    }

    [Fact]
    public async Task Chapter_card_marks_current_configuration_completeness_as_unavailable()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 1, 4, 4096)]
        };
        workspaceService.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 0, 4, 4096, null)
        ];
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal("当前配置完整度：不可用", Assert.Single(viewModel.Chapters).CompletenessText);
    }

    [Fact]
    public async Task Switching_books_clears_chapter_selection_without_cross_book_carryover()
    {
        var workspaceService = CreateTwoBookWorkspace();
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);

        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[1]);

        Assert.Empty(viewModel.SelectedChapterIndices);
        Assert.DoesNotContain(viewModel.Chapters, chapter => chapter.IsSelected);
        Assert.Equal("第二本", viewModel.SelectedBookTitle);
    }

    [Fact]
    public async Task Clear_selected_chapters_uses_one_application_batch_request()
    {
        var workspaceService = CreateTwoBookWorkspace();
        workspaceService.ClearChaptersResult = new CacheCleanupResult(2048, 2, 0, 0);
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);
        viewModel.HandleChapterClick(viewModel.Chapters[1], DesktopSelectionModifiers.Control);

        await viewModel.ClearSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal(("book-1", new[] { 0, 1 }), workspaceService.LastClearChaptersRequest);
        Assert.Equal(1, workspaceService.ClearChaptersCallCount);
        Assert.Empty(viewModel.SelectedChapterIndices);
    }

    [Fact]
    public async Task Selecting_all_chapters_cleans_the_whole_visible_book_through_batch_boundary()
    {
        var workspaceService = CreateTwoBookWorkspace();
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleSelectAllChapters();

        await viewModel.ClearSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal(("book-1", new[] { 0, 1 }), workspaceService.LastClearChaptersRequest);
        Assert.Equal(0, workspaceService.ClearBookCallCount);
    }

    private static FakeCacheWorkspaceService CreateTwoBookWorkspace()
    {
        var workspace = new FakeCacheWorkspaceService
        {
            BooksResult =
            [
                new CachedBookCacheItem("book-1", "第一本", "作者甲", 2, 2, 2048),
                new CachedBookCacheItem("book-2", "第二本", "作者乙", 1, 1, 1024)
            ]
        };
        workspace.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 1, "第二章", 1, 1, 1024, 1)
        ];
        workspace.ChaptersResult["book-2"] =
        [
            new CachedChapterCacheItem("book-2", 0, "另一章", 1, 1, 1024, 1)
        ];
        return workspace;
    }

    [Fact]
    public async Task SelectBookAsync_on_bound_page_loads_async_chapters_without_error()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var workspaceService = new FakeCacheWorkspaceService
            {
                BooksResult =
                [
                    new CachedBookCacheItem("book-1", "第一本", "作者甲", 1, 1, 1024)
                ],
                LoadChaptersOnBackgroundThread = true
            };
            workspaceService.ChaptersResult["book-1"] =
            [
                new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1)
            ];
            var feedbackService = new FakeFeedbackService();
            var viewModel = CreateViewModel(workspaceService, feedbackService: feedbackService);
            var page = new CacheManagementPage(viewModel);
            page.Measure(new System.Windows.Size(1280, 820));
            page.Arrange(new System.Windows.Rect(0, 0, 1280, 820));
            page.UpdateLayout();

            await viewModel.LoadAsync(CancellationToken.None);
            await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

            Assert.Null(feedbackService.LastTitle);
            Assert.Single(viewModel.Chapters);
            Assert.Equal("第一章", viewModel.Chapters[0].Title);
        });
    }

    private static CacheManagementViewModel CreateViewModel(
        FakeCacheWorkspaceService workspaceService,
        FakeFeedbackService? feedbackService = null,
        FakeAppDialogService? dialogService = null)
    {
        return new CacheManagementViewModel(
            workspaceService,
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            new FakeNavigationService());
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        private readonly Queue<IReadOnlyList<CachedBookCacheItem>> _booksQueue = new();

        public IReadOnlyList<CachedBookCacheItem> BooksResult { get; set; } = [];

        public IReadOnlyList<IReadOnlyList<CachedBookCacheItem>>? BooksSequence
        {
            set
            {
                _booksQueue.Clear();
                if (value is null)
                {
                    return;
                }

                foreach (var item in value)
                {
                    _booksQueue.Enqueue(item);
                }
            }
        }

        public Dictionary<string, CachedChapterCacheItem[]> ChaptersResult { get; } = [];

        public Dictionary<string, TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>> PendingChapterTasks { get; } = [];

        public bool LoadChaptersOnBackgroundThread { get; set; }

        public CacheCleanupResult ClearBookResult { get; set; } = new(0, 0, 0, 0);

        public CacheCleanupResult ClearChaptersResult { get; set; } = new(0, 0, 0, 0);

        public (string BookId, int[] ChapterIndices)? LastClearChaptersRequest { get; private set; }

        public int ClearChaptersCallCount { get; private set; }

        public int ClearBookCallCount { get; private set; }

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken)
        {
            if (_booksQueue.Count > 0)
            {
                BooksResult = _booksQueue.Dequeue();
            }

            return Task.FromResult(BooksResult);
        }

        public async Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken)
        {
            if (PendingChapterTasks.TryGetValue(bookId, out var pendingTask))
            {
                return await pendingTask.Task.WaitAsync(cancellationToken);
            }

            if (LoadChaptersOnBackgroundThread)
            {
                return await Task.Run<IReadOnlyList<CachedChapterCacheItem>>(
                    () => ChaptersResult.TryGetValue(bookId, out var backgroundChapters)
                        ? backgroundChapters
                        : [],
                    cancellationToken);
            }

            return ChaptersResult.TryGetValue(bookId, out var chapters)
                ? chapters
                : [];
        }

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
        {
            ClearBookCallCount++;
            return Task.FromResult(ClearBookResult);
        }

        public Task<CacheCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            ClearChaptersCallCount++;
            LastClearChaptersRequest = (bookId, chapterIndices.ToArray());
            return Task.FromResult(ClearChaptersResult);
        }

        public Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Confirm;

        public string? LastPrimaryButtonText { get; private set; }

        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
        {
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
            return Task.FromResult(UnsavedChangesDecision.Cancel);
        }
    }

    private sealed class FakeNavigationService : ITestNavigationService
    {
        public INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => true;
        public bool Navigate(Type pageType) => true;
        public bool Navigate(Type pageType, object? dataContext) => true;
        public bool Navigate(string pageIdOrTargetTag) => true;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;
        public bool NavigateWithHierarchy(Type pageType) => true;
        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;
        public void SetNavigationControl(INavigationView navigation) { }
    }
}
