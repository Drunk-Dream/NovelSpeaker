using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

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
    public async Task ClearBookAsync_when_selected_book_removed_keeps_empty_context_and_warns()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksSequence =
            [
                [new CachedBookCacheItem("book-1", "第一本", "作者甲", 1, 2, 1024)],
                []
            ],
            ClearBookResult = new CacheCleanupResult(1024, 2, 1, 0)
        };
        workspaceService.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 1, 2, 1024, 2)
        ];
        var feedbackService = new FakeFeedbackService();
        var viewModel = CreateViewModel(
            workspaceService,
            feedbackService: feedbackService,
            dialogService: new FakeAppDialogService { NextConfirmationDecision = AppConfirmationDecision.Confirm });
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        await viewModel.ClearBookCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSelection);
        Assert.True(viewModel.ShowSelectedBookEmptyState);
        Assert.Empty(viewModel.Books);
        Assert.Equal("缓存已部分清理", feedbackService.LastTitle);
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

        public CacheCleanupResult ClearBookResult { get; set; } = new(0, 0, 0, 0);

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

            return ChaptersResult.TryGetValue(bookId, out var chapters)
                ? chapters
                : [];
        }

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult(ClearBookResult);

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

    private sealed class FakeNavigationService : INavigationService
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
