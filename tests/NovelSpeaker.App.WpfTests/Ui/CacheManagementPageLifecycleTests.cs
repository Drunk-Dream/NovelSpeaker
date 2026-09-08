using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.WpfTests.TestDoubles;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CacheManagementPageLifecycleTests
{
    [Fact]
    public async Task Cache_management_page_lifecycle_owns_invalidation_subscription()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var workspace = new CachePageWorkspace
            {
                Books = [new CachedBookCacheItem("book-1", "第一本", null, 1, 1, 1024)]
            };
            var page = new CacheManagementPage(CreateViewModel(workspace));

            await page.OnNavigatedToAsync();
            Assert.Equal(1, workspace.InvalidationSubscriptionCount);

            await page.OnNavigatedFromAsync();
            Assert.Equal(0, workspace.InvalidationSubscriptionCount);
        });
    }

    [Fact]
    public async Task SelectBookAsync_on_bound_page_loads_async_chapters_without_error()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var workspace = new CachePageWorkspace
            {
                Books = [new CachedBookCacheItem("book-1", "第一本", "作者甲", 1, 1, 1024)],
                Chapters = [new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1)],
                LoadOnBackgroundThread = true
            };
            var feedback = new CachePageFeedback();
            var viewModel = CreateViewModel(workspace, feedback);
            var page = new CacheManagementPage(viewModel);
            page.Measure(new System.Windows.Size(1280, 820));
            page.Arrange(new System.Windows.Rect(0, 0, 1280, 820));
            page.UpdateLayout();

            await viewModel.LoadAsync(CancellationToken.None);
            await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

            Assert.Null(feedback.LastTitle);
            Assert.Equal("第一章", Assert.Single(viewModel.Chapters).Title);
        });
    }

    private static CacheManagementViewModel CreateViewModel(
        CachePageWorkspace workspace,
        CachePageFeedback? feedback = null) =>
        new(
            workspace,
            new CachePageCatalog(workspace),
            new CachePageCoverageQuery(workspace),
            workspace.InvalidationCoordinator,
            feedback ?? new CachePageFeedback(),
            new CachePageDialog(),
            new CachePageNavigator(),
            new WpfFakeChapterExportCoordinator(),
            new CachePageFileDialogs());

    private sealed class CachePageCatalog(CachePageWorkspace workspace) : ICacheCatalog
    {
        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(CancellationToken cancellationToken) =>
            (await workspace.GetCachedBooksAsync(cancellationToken))
                .Select(book => new CachedBookSummary(book.BookId, book.Title, book.Author, book.ChapterCount, book.EntryCount, book.TotalSizeBytes))
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
            (await GetCachedChaptersAsync(bookId, [chapterIndex], cancellationToken)).FirstOrDefault();
    }

    private sealed class CachePageCoverageQuery(CachePageWorkspace workspace) : ICacheCoverageQuery
    {
        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChapterCacheStatus>>(
                workspace.Chapters
                    .Where(chapter => chapter.BookId == bookId && chapterIndices.Contains(chapter.ChapterIndex))
                    .Select(chapter => new ChapterCacheStatus(
                        chapter.ChapterIndex,
                        chapter.CachedSegmentCount,
                        chapter.CurrentConfigurationSegmentCount)
                    {
                        Kind = chapter.CurrentConfigurationStatus
                    })
                    .ToArray());

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            IReadOnlyCollection<NovelSpeaker.Application.Playback.PlaybackChapterMetadata> chapters,
            CancellationToken cancellationToken) =>
            GetAsync(bookId, chapterIndices, cancellationToken);
    }

    private sealed class CachePageWorkspace : ICacheWorkspaceService
    {
        private EventHandler<CacheChangedEventArgs>? _changed;

        public IReadOnlyList<CachedBookCacheItem> Books { get; init; } = [];

        public IReadOnlyList<CachedChapterCacheItem> Chapters { get; init; } = [];

        public bool LoadOnBackgroundThread { get; init; }

        public int SubscriptionCount { get; private set; }

        public FakeCacheInvalidationCoordinator InvalidationCoordinator { get; } = new();

        public int InvalidationSubscriptionCount => InvalidationCoordinator.SubscriptionCount;

        event EventHandler<CacheChangedEventArgs>? ICacheWorkspaceService.Changed
        {
            add
            {
                _changed += value;
                SubscriptionCount++;
            }
            remove
            {
                _changed -= value;
                SubscriptionCount--;
            }
        }

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Books);

        public Task<CachedBookCacheItem?> GetCachedBookAsync(string bookId, CancellationToken cancellationToken) =>
            Task.FromResult(Books.FirstOrDefault(book => book.BookId == bookId));

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(
            string bookId,
            CancellationToken cancellationToken)
        {
            var chapters = Chapters.Where(chapter => chapter.BookId == bookId).ToArray();
            return LoadOnBackgroundThread
                ? Task.Run<IReadOnlyList<CachedChapterCacheItem>>(() => chapters, cancellationToken)
                : Task.FromResult<IReadOnlyList<CachedChapterCacheItem>>(chapters);
        }

        public Task<CachedChapterCacheItem?> GetCachedChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Chapters.FirstOrDefault(chapter =>
                    chapter.BookId == bookId && chapter.ChapterIndex == chapterIndex));
        }

        public Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChapterCacheStatus>>([]);

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken) =>
            Task.FromResult(new CacheCleanupResult(0, 0, 0, 0));

        public Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) =>
            Task.FromResult(new CacheCleanupResult(0, 0, 0, 0));

        public Task<CacheCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CacheCleanupResult(0, 0, 0, 0));

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CacheCleanupResult(0, 0, 0, 0));
    }

    private sealed class FakeCacheInvalidationCoordinator : ICacheInvalidationCoordinator
    {
        private EventHandler<CacheInvalidationBatch>? _batchPublished;

        public event EventHandler<CacheInvalidationBatch>? BatchPublished
        {
            add
            {
                _batchPublished += value;
                SubscriptionCount++;
            }
            remove
            {
                _batchPublished -= value;
                SubscriptionCount--;
            }
        }

        public int SubscriptionCount { get; private set; }

        public void Publish(CacheInvalidation invalidation) =>
            _batchPublished?.Invoke(this, new CacheInvalidationBatch([invalidation]));

        public Task FlushPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CachePageFeedback : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public ProjectedUiError Project(Exception exception) =>
            new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected) => LastTitle = title;

        public void ShowSuccess(string title, string message) => LastTitle = title;

        public void ShowWarning(string title, string message) => LastTitle = title;

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken) =>
            Task.FromResult(AppConfirmationDecision.Cancel);
    }

    private sealed class CachePageDialog : IAppDialogService
    {
        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken) =>
            Task.FromResult(AppConfirmationDecision.Cancel);

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
            string title,
            string message,
            string saveButtonText,
            string discardButtonText,
            string cancelButtonText,
            CancellationToken cancellationToken) =>
            Task.FromResult(UnsavedChangesDecision.Cancel);
    }

    private sealed class CachePageNavigator : IAppNavigator
    {
        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(true);

        public AppRoute CurrentRoute => AppRoutes.CacheManagement;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(true);
    }

    private sealed class CachePageFileDialogs : IPresentationFileDialogService
    {
        public Task<string?> PickOpenFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(PresentationFolderDialogOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

}
