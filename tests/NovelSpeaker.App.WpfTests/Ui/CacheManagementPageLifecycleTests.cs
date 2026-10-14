using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation.Platform;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CacheManagementPageLifecycleTests
{
    [Fact]
    public async Task Large_book_selector_virtualizes_and_selects_remote_books_without_losing_chapter_navigation()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            const int count = 10_000;
            var readModel = new CacheReadModelTestDouble
            {
                Books = Enumerable.Range(0, count).Select(index =>
                    new CachedBookSummary($"book-{index:D5}", $"Synthetic book {index}", null, 1, 1, 1024)).ToArray()
            };
            var tailBookId = readModel.Books[^1].BookId;
            readModel.ChaptersByBook[tailBookId] = Enumerable.Range(0, count).Select(index =>
                new CacheChapterView(index, new(tailBookId, index, $"Chapter {index}", 1, 1, 1024), new(index, 1, 1))).ToArray();
            var page = new CacheManagementPage(CreateViewModel(new CacheStoreTestDouble(), readModel));
            using var host = WpfWindowHost.ForControl(page);
            host.Window.Width = 1280;
            host.Window.Height = 760;
            WpfWindowHost.Show(host.Window);
            await page.OnNavigatedToAsync();
            host.Window.UpdateLayout();

            var books = Assert.IsType<ListBox>(page.FindName("BooksListBox"));
            Assert.Equal(count, books.Items.Count);
            AssertBoundedContainers(books);
            host.Window.Activate();
            var first = Assert.IsType<ListBoxItem>(books.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.True(VisualTreeTestHelper.FindDescendant<Button>(first)!.Focus());
            for (var index = 1; index <= 10; index++)
            {
                var focused = Assert.IsType<Button>(Keyboard.FocusedElement);
                PressTab(focused);
                Assert.Equal($"book-{index:D5}", Assert.IsType<CachedBookListItemViewModel>(
                    Assert.IsType<Button>(Keyboard.FocusedElement).DataContext).BookId);
            }
            AssertBoundedContainers(books);
            books.ScrollIntoView(page.ViewModel.Books[^1]);
            await WpfTestHost.DrainDispatcherAsync(DispatcherPriority.ContextIdle);
            host.Window.UpdateLayout();
            AssertBoundedContainers(books);
            var tail = Assert.IsType<ListBoxItem>(books.ItemContainerGenerator.ContainerFromIndex(count - 1));
            var button = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(tail));
            Assert.Equal(tailBookId, Assert.IsType<CachedBookListItemViewModel>(button.DataContext).BookId);
            Assert.Same(page.ViewModel.SelectBookCommand, button.Command);
            await page.ViewModel.SelectBookCommand.ExecuteAsync(button.CommandParameter);
            host.Window.UpdateLayout();
            Assert.True(page.ViewModel.Books[^1].IsSelected);
            Assert.Equal(count, page.ViewModel.Chapters.Count);

            var chapters = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
            AssertBoundedContainers(chapters);
            chapters.ScrollIntoView(page.ViewModel.Chapters[^1]);
            await WpfTestHost.DrainDispatcherAsync(DispatcherPriority.ContextIdle);
            host.Window.UpdateLayout();
            AssertBoundedContainers(chapters);
            Assert.NotNull(chapters.ItemContainerGenerator.ContainerFromIndex(count - 1));

            books.ScrollIntoView(page.ViewModel.Books[0]);
            host.Window.UpdateLayout();
            books.ScrollIntoView(page.ViewModel.Books[^1]);
            host.Window.UpdateLayout();
            tail = Assert.IsType<ListBoxItem>(books.ItemContainerGenerator.ContainerFromIndex(count - 1));
            Assert.True(Assert.IsType<CachedBookListItemViewModel>(tail.DataContext).IsSelected);
            host.Window.Height = 620;
            host.Window.UpdateLayout();
            AssertBoundedContainers(books);

            await page.OnNavigatedFromAsync();
            Assert.Equal(0, readModel.SubscriberCount);
            await page.OnNavigatedToAsync();
            host.Window.UpdateLayout();
            Assert.Equal(1, readModel.SubscriberCount);
            Assert.False(page.ViewModel.HasSelection);
            AssertBoundedContainers(books);
            await page.OnNavigatedFromAsync();
            Assert.Equal(0, readModel.SubscriberCount);
        });
    }

    private static void AssertBoundedContainers(ListBox list)
    {
        var realized = Enumerable.Range(0, list.Items.Count)
            .Count(index => list.ItemContainerGenerator.ContainerFromIndex(index) is not null);
        Assert.InRange(realized, 1, list.Items.Count / 10);
    }

    private static void PressTab(Button button)
    {
        var key = new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(button)!, Environment.TickCount, Key.Tab)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        button.RaiseEvent(key);
        Assert.True(key.Handled);
    }

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
