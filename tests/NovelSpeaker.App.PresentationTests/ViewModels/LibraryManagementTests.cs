using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.TestKit.Books;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.Books.Shared;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed partial class LibraryViewModelTests
{
    private static BookSummary[] ManagementBooks =>
    [
        new("a", "Alpha", null, "Chapter", DateTimeOffset.UtcNow),
        new("b", "Beta", null, "Chapter", DateTimeOffset.UtcNow),
        new("c", "Gamma", null, "Chapter", DateTimeOffset.UtcNow)
    ];

    [Fact]
    public async Task Management_clicks_do_not_open_books_and_hidden_items_leave_selection()
    {
        var navigation = new FakeNavigationService();
        var viewModel = CreateViewModel(catalogService: new FakeBookCatalogService(ManagementBooks), navigationService: navigation);
        await viewModel.LoadAsync(CancellationToken.None);
        var escapeHandler = Assert.IsAssignableFrom<ITransientEscapeHandler>(viewModel);
        Assert.False(escapeHandler.TryHandleEscape());
        Assert.True(viewModel.HandleBookClick(viewModel.Books[0], DesktopSelectionModifiers.Control));
        await viewModel.OpenBookCommand.ExecuteAsync(viewModel.Books[1]);
        Assert.True(viewModel.IsManagementMode);
        Assert.Equal(2, viewModel.SelectedBookCount);
        viewModel.SearchText = "Gamma";
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.Equal(0, viewModel.SelectedBookCount);
        Assert.True(viewModel.IsManagementMode);
        Assert.Null(navigation.LastNavigationRoute);
        Assert.True(escapeHandler.TryHandleEscape());
        Assert.False(viewModel.IsManagementMode);
        Assert.Equal(0, viewModel.SelectedBookCount);
        Assert.False(escapeHandler.TryHandleEscape());
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        Assert.True(Assert.Single(viewModel.Books).IsSelected);
        Assert.True(escapeHandler.TryHandleEscape());
        Assert.False(viewModel.IsManagementMode);
        Assert.Equal(0, viewModel.SelectedBookCount);
        Assert.False(Assert.Single(viewModel.Books).IsSelected);
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        viewModel.HandleNavigatedFrom();
        Assert.False(viewModel.IsManagementMode);
        Assert.Equal(0, viewModel.SelectedBookCount);
        viewModel.HandleNavigatedTo(new PageActivationController().Activate());
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        Assert.Equal(1, viewModel.SelectedBookCount);
        viewModel.HandleNavigatedFrom();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Cancelled_batch_stops_page_work_without_completion_notification(int cancelAfterCount)
    {
        var changes = new FakeBookChanges();
        var deletion = new FakeBookManagementService();
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(catalogService: new FakeBookCatalogService(ManagementBooks), managementService: deletion,
            deleteDialogService: new FakeBookDeleteDialogService { NextResult = new(true, true) }, bookChanges: changes, feedback: feedback);
        await viewModel.LoadAsync(CancellationToken.None);
        deletion.OnDelete = _ =>
        {
            if (deletion.Requests.Count == cancelAfterCount) viewModel.HandleNavigatedFrom();
        };
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        await viewModel.DeleteSelectedBooksCommand.ExecuteAsync(null);
        Assert.Equal(cancelAfterCount, deletion.Requests.Count);
        Assert.Empty(feedback.Notifications);
    }

    [Theory]
    [InlineData(null, null, 3, 0, 0, UiMessageSeverity.Information)]
    [InlineData("b", null, 2, 0, 1, UiMessageSeverity.Warning)]
    [InlineData(null, "c", 2, 1, 0, UiMessageSeverity.Warning)]
    public async Task Batch_delete_confirms_once_and_reports_results_after_processing_all_books(
        string? failingBookId, string? missingBookId, int succeeded, int skipped, int failed, UiMessageSeverity severity)
    {
        var dialog = new FakeBookDeleteDialogService { NextResult = new(true, true) };
        var deletion = new FakeBookManagementService { FailingBookId = failingBookId, MissingBookId = missingBookId };
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(catalogService: new FakeBookCatalogService(ManagementBooks), managementService: deletion, deleteDialogService: dialog, feedback: feedback);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        await viewModel.DeleteSelectedBooksCommand.ExecuteAsync(null);
        Assert.Equal(3, Assert.Single(dialog.Requests).BookCount);
        Assert.Equal(["a", "b", "c"], deletion.Requests.Select(request => request.BookId).Order());
        Assert.Equal(("删除完成", $"成功 {succeeded}，跳过 {skipped}，失败 {failed}。", severity), Assert.Single(feedback.Notifications));
        viewModel.HandleNavigatedFrom();
    }

    [Theory]
    [InlineData(null, null, 3, 0, 0, UiMessageSeverity.Information)]
    [InlineData("b", "c", 1, 1, 1, UiMessageSeverity.Warning)]
    [InlineData(null, "c", 2, 1, 0, UiMessageSeverity.Warning)]
    public async Task Batch_export_picks_one_folder_and_reports_results_after_processing_all_books(
        string? failingBookId, string? unavailableBookId, int succeeded, int skipped, int failed, UiMessageSeverity severity)
    {
        var exporter = new RecordingBookTextExporter { FailingBookId = failingBookId, UnavailableBookId = unavailableBookId };
        var dialogs = new RecordingFolderPicker();
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(catalogService: new FakeBookCatalogService(ManagementBooks), textExporter: exporter, fileDialogs: dialogs, feedback: feedback);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        await viewModel.ExportBooksCommand.ExecuteAsync(null);
        Assert.Equal(1, dialogs.FolderRequests);
        Assert.Equal(["a", "b", "c"], exporter.Books.Order());
        Assert.Equal(("导出完成", $"成功 {succeeded}，跳过 {skipped}，失败 {failed}。", severity), Assert.Single(feedback.Notifications));
        viewModel.HandleNavigatedFrom();
    }

    [Theory]
    [InlineData("a", 1, 0, 0, UiMessageSeverity.Information)]
    [InlineData("b", 0, 0, 1, UiMessageSeverity.Warning)]
    [InlineData("c", 0, 1, 0, UiMessageSeverity.Warning)]
    public async Task Single_book_export_reports_success_unavailable_or_failure(
        string bookId, int succeeded, int skipped, int failed, UiMessageSeverity severity)
    {
        var exporter = new RecordingBookTextExporter();
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(catalogService: new FakeBookCatalogService(ManagementBooks), textExporter: exporter,
            fileDialogs: new RecordingFolderPicker(), feedback: feedback);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ExportBooksCommand.ExecuteAsync(viewModel.Books.Single(book => book.BookId == bookId));

        Assert.Equal([bookId], exporter.Books);
        Assert.Equal(("导出完成", $"成功 {succeeded}，跳过 {skipped}，失败 {failed}。", severity), Assert.Single(feedback.Notifications));
        viewModel.HandleNavigatedFrom();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Cancelled_export_does_not_send_completion_notification(int cancelAfterIndex)
    {
        var feedback = new FakeFeedbackService();
        var exporter = new RecordingBookTextExporter { FailingBookId = null, UnavailableBookId = null };
        var dialogs = new RecordingFolderPicker { Directory = cancelAfterIndex < 0 ? null : "destination" };
        var viewModel = CreateViewModel(catalogService: new FakeBookCatalogService(ManagementBooks), textExporter: exporter,
            fileDialogs: dialogs, feedback: feedback);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        var cancellationBookId = cancelAfterIndex < 0 ? null : viewModel.Books[cancelAfterIndex].BookId;
        exporter.OnExport = bookId =>
        {
            if (bookId == cancellationBookId) viewModel.HandleNavigatedFrom();
        };

        await viewModel.ExportBooksCommand.ExecuteAsync(null);

        Assert.Empty(feedback.Notifications);
        Assert.Equal(cancelAfterIndex + 1, exporter.Books.Count);
        viewModel.HandleNavigatedFrom();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Single_book_delete_notifies_when_book_is_missing_or_deletion_fails(bool fails)
    {
        var catalog = new FakeBookCatalogService(ManagementBooks);
        var feedback = new FakeFeedbackService();
        var deletion = new FakeBookManagementService { FailingBookId = fails ? "a" : null, MissingBookId = fails ? null : "a" };
        var viewModel = CreateViewModel(catalogService: catalog, managementService: deletion, feedback: feedback,
            deleteDialogService: new FakeBookDeleteDialogService { NextResult = new(true, true) });
        await viewModel.LoadAsync(CancellationToken.None);
        var book = viewModel.Books.Single(book => book.BookId == "a");
        catalog.Books = fails ? ManagementBooks : ManagementBooks.Where(book => book.Id != "a").ToArray();

        await viewModel.DeleteBookCommand.ExecuteAsync(book);

        var notification = Assert.Single(feedback.Notifications);
        Assert.Equal(fails ? "删除失败" : "书籍已不存在", notification.Title);
        Assert.Equal(fails ? UiMessageSeverity.Error : UiMessageSeverity.Warning, notification.Severity);
        Assert.Equal(fails, viewModel.Books.Any(book => book.BookId == "a"));
        viewModel.HandleNavigatedFrom();
    }

    private sealed class RecordingBookTextExporter : IBookTextExportService
    {
        public List<string> Books { get; } = [];
        public string? FailingBookId { get; set; } = "b";
        public string? UnavailableBookId { get; set; } = "c";
        public Action<string>? OnExport { get; set; }
        public Task<bool> ExportAsync(string bookId, string directory, CancellationToken cancellationToken)
        {
            Books.Add(bookId);
            Assert.Equal("destination", directory);
            OnExport?.Invoke(bookId);
            if (bookId == FailingBookId) throw new IOException("test failure");
            return Task.FromResult(bookId != UnavailableBookId);
        }
    }

    private sealed class RecordingFolderPicker : IPresentationFileDialogService
    {
        public int FolderRequests { get; private set; }
        public string? Directory { get; set; } = "destination";
        public Task<string?> PickFolderAsync(PresentationFolderDialogOptions options, CancellationToken cancellationToken)
        {
            FolderRequests++;
            return Task.FromResult(Directory);
        }
        public Task<string?> PickOpenFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> PickSaveFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
