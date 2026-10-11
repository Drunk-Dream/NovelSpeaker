using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation.Platform;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CacheManagementPageLifecycleTests
{
    [Fact]
    public async Task Cache_management_page_lifecycle_owns_read_model_subscription()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var store = new CacheStoreTestDouble();
            var readModel = new CacheReadModelTestDouble
            {
                Books = [new CachedBookSummary("book-1", "第一本", null, 1, 1, 1024)]
            };
            var page = new CacheManagementPage(CreateViewModel(
                store,
                readModel));

            await page.OnNavigatedToAsync();
            Assert.Equal(1, readModel.SubscriberCount);

            await page.OnNavigatedFromAsync();
            Assert.Equal(0, readModel.SubscriberCount);
        });
    }

    private static CacheManagementViewModel CreateViewModel(
        CacheStoreTestDouble store,
        CacheReadModelTestDouble readModel) =>
        new(
            store,
            readModel,
            new CachePageFeedback(),
            new CachePageDialog(),
            new CachePageNavigator(),
            new FakeChapterExportCoordinator(),
            new CachePageFileDialogs());

    private sealed class CachePageFeedback : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception) =>
            new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

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
