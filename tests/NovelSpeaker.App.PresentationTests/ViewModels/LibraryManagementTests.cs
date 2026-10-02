using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.Books.Shared;
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
        Assert.True(viewModel.HandleBookClick(viewModel.Books[0], DesktopSelectionModifiers.Control));
        await viewModel.OpenBookCommand.ExecuteAsync(viewModel.Books[1]);
        Assert.True(viewModel.IsManagementMode);
        Assert.Equal(2, viewModel.SelectedBookCount);
        viewModel.SearchText = "Gamma";
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.Equal(0, viewModel.SelectedBookCount);
        Assert.True(viewModel.IsManagementMode);
        Assert.Null(navigation.LastNavigationRoute);
        viewModel.SelectAllBooksCommand.Execute(null);
        Assert.True(Assert.Single(viewModel.Books).IsSelected);
        viewModel.HandleNavigatedFrom();
        Assert.False(viewModel.IsManagementMode);
        Assert.Equal(0, viewModel.SelectedBookCount);
        viewModel.HandleNavigatedTo();
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        Assert.Equal(1, viewModel.SelectedBookCount);
        viewModel.HandleNavigatedFrom();
    }

    [Fact]
    public async Task Cancelled_batch_keeps_catalog_invalidated_after_partial_deletion()
    {
        var invalidation = new BookCatalogInvalidationState();
        var deletion = new FakeBookManagementService();
        var viewModel = CreateViewModel(catalogService: new FakeBookCatalogService(ManagementBooks), managementService: deletion,
            deleteDialogService: new FakeBookDeleteDialogService { NextResult = new(true, true) }, invalidation: invalidation);
        await viewModel.LoadAsync(CancellationToken.None);
        deletion.OnDelete = _ => viewModel.HandleNavigatedFrom();
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        await viewModel.DeleteSelectedBooksCommand.ExecuteAsync(null);
        Assert.Single(deletion.Requests);
        Assert.True(invalidation.IsInvalidated);
    }

    [Fact]
    public async Task Batch_delete_confirms_once_and_continues_after_failure()
    {
        var dialog = new FakeBookDeleteDialogService { NextResult = new(true, true) };
        var deletion = new FakeBookManagementService { FailingBookId = "b" };
        var viewModel = CreateViewModel(catalogService: new FakeBookCatalogService(ManagementBooks), managementService: deletion, deleteDialogService: dialog);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        await viewModel.DeleteSelectedBooksCommand.ExecuteAsync(null);
        Assert.Equal(3, Assert.Single(dialog.Requests).BookCount);
        Assert.Equal(["a", "b", "c"], deletion.Requests.Select(request => request.BookId).Order());
        Assert.Contains("成功 2", viewModel.StatusMessage);
        Assert.Contains("失败 1", viewModel.StatusMessage);
        viewModel.HandleNavigatedFrom();
    }

    [Fact]
    public async Task Batch_export_picks_one_folder_and_reports_unavailable_and_failed_books()
    {
        var exporter = new RecordingBookTextExporter();
        var dialogs = new RecordingFolderPicker();
        var viewModel = CreateViewModel(catalogService: new FakeBookCatalogService(ManagementBooks), textExporter: exporter, fileDialogs: dialogs);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.EnterManagementCommand.Execute(null);
        viewModel.SelectAllBooksCommand.Execute(null);
        await viewModel.ExportBooksCommand.ExecuteAsync(null);
        Assert.Equal(1, dialogs.FolderRequests);
        Assert.Equal(["a", "b", "c"], exporter.Books.Order());
        Assert.Contains("成功 1，跳过 1，失败 1", viewModel.StatusMessage);
        viewModel.HandleNavigatedFrom();
    }

    private sealed class RecordingBookTextExporter : IBookTextExportService
    {
        public List<string> Books { get; } = [];
        public Task<bool> ExportAsync(string bookId, string directory, CancellationToken cancellationToken)
        {
            Books.Add(bookId);
            Assert.Equal("destination", directory);
            if (bookId == "b") throw new IOException("test failure");
            return Task.FromResult(bookId != "c");
        }
    }

    private sealed class RecordingFolderPicker : IPresentationFileDialogService
    {
        public int FolderRequests { get; private set; }
        public Task<string?> PickFolderAsync(PresentationFolderDialogOptions options, CancellationToken cancellationToken)
        {
            FolderRequests++;
            return Task.FromResult<string?>("destination");
        }
        public Task<string?> PickOpenFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> PickSaveFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
