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
            var cache = new CachePresentationTestDouble
            {
                Books = [new CachedBookSummary("book-1", "第一本", null, 1, 1, 1024)]
            };
            var page = new CacheManagementPage(CreateViewModel(cache));

            await page.OnNavigatedToAsync();
            Assert.Equal(1, cache.InvalidationSubscriptionCount);

            await page.OnNavigatedFromAsync();
            Assert.Equal(0, cache.InvalidationSubscriptionCount);
        });
    }

    [Fact]
    public async Task SelectBookAsync_on_bound_page_loads_async_chapters_without_error()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var cache = new CachePresentationTestDouble
            {
                Books = [new CachedBookSummary("book-1", "第一本", "作者甲", 1, 1, 1024)],
                Statuses = [new ChapterCacheStatus(0, 1, 1)]
            };
            cache.ChaptersByBook["book-1"] =
            [new CachedChapterSummary("book-1", 0, "第一章", 1, 1, 1024)];
            var feedback = new CachePageFeedback();
            var viewModel = CreateViewModel(cache, feedback);
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
        CachePresentationTestDouble cache,
        CachePageFeedback? feedback = null) =>
        new(
            cache,
            cache,
            cache,
            cache,
            feedback ?? new CachePageFeedback(),
            new CachePageDialog(),
            new CachePageNavigator(),
            new WpfFakeChapterExportCoordinator(),
            new CachePageFileDialogs());

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
