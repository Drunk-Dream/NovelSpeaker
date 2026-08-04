using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CacheManagementPageLifecycleTests
{
    [Fact]
    public async Task Cache_management_page_lifecycle_owns_cache_change_subscription()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var workspace = new CachePageWorkspace
            {
                Books = [new CachedBookCacheItem("book-1", "第一本", null, 1, 1, 1024)]
            };
            var page = new CacheManagementPage(CreateViewModel(workspace));

            await page.OnNavigatedToAsync();
            Assert.Equal(1, workspace.SubscriptionCount);

            await page.OnNavigatedFromAsync();
            Assert.Equal(0, workspace.SubscriptionCount);
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
            feedback ?? new CachePageFeedback(),
            new CachePageDialog(),
            new CachePageNavigator(),
            new CachePageExporter(),
            new CachePageFileDialogs(),
            new CachePageLauncher());

    private sealed class CachePageWorkspace : ICacheWorkspaceService
    {
        private EventHandler<CacheChangedEventArgs>? _changed;

        public IReadOnlyList<CachedBookCacheItem> Books { get; init; } = [];

        public IReadOnlyList<CachedChapterCacheItem> Chapters { get; init; } = [];

        public bool LoadOnBackgroundThread { get; init; }

        public int SubscriptionCount { get; private set; }

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

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(
            string bookId,
            CancellationToken cancellationToken)
        {
            var chapters = Chapters.Where(chapter => chapter.BookId == bookId).ToArray();
            return LoadOnBackgroundThread
                ? Task.Run<IReadOnlyList<CachedChapterCacheItem>>(() => chapters, cancellationToken)
                : Task.FromResult<IReadOnlyList<CachedChapterCacheItem>>(chapters);
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

        public Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(true);
    }

    private sealed class CachePageExporter : IExportChaptersService
    {
        public Task<ExportChaptersResult> ExportAsync(ExportChaptersRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(ExportChaptersResult.Failed(ExportChaptersStatus.IncompleteCache, 0));
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

    private sealed class CachePageLauncher : IPresentationLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
