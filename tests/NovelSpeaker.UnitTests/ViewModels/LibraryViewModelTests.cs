using NovelSpeaker.Application.Books;
using NovelSpeaker.App.ViewModels;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task ImportFileAsync_prepares_preview_and_waits_for_confirmation()
    {
        var importService = new FakeBookImportService(
            new BookImportAnalysis(
                BookImportAnalysisStatus.ReadyToCommit,
                "C:\\books\\demo.txt",
                "demo.txt",
                "demo",
                "utf-8",
                "preview",
                "第一章 开始\n正文",
                "hash",
                [new BookImportChapter(0, 0, "第一章 开始", 6, 2)],
                null,
                null,
                "魔性沧月",
                true),
            new BookImportResult("book-1", "demo", 1));

        var catalogService = new FakeBookCatalogService([
            new BookSummary("book-1", "demo", null, "第一章 开始", DateTime.UtcNow.ToString("O"))
        ]);

        var viewModel = new LibraryViewModel(importService, catalogService);

        await viewModel.ImportFileAsync("C:\\books\\demo.txt", CancellationToken.None);

        Assert.Empty(viewModel.Books);
        Assert.True(viewModel.CanConfirmImport);
        Assert.True(viewModel.IsEncodingPreviewVisible);
        Assert.Equal("preview", viewModel.PreviewText);
        Assert.Equal("demo", viewModel.SuggestedTitle);
        Assert.Equal("魔性沧月", viewModel.SuggestedAuthor);
        Assert.True(viewModel.IsFileNameTemplateMatched);
        Assert.Equal(0, importService.CommitCallCount);
    }

    [Fact]
    public async Task ConfirmImportAsync_commits_pending_analysis_and_refreshes_books()
    {
        var importService = new FakeBookImportService(
            new BookImportAnalysis(
                BookImportAnalysisStatus.ReadyToCommit,
                "C:\\books\\demo.txt",
                "demo.txt",
                "demo",
                "utf-8",
                "preview",
                "第一章 开始\n正文",
                "hash",
                [new BookImportChapter(0, 0, "第一章 开始", 6, 2)],
                null,
                null,
                "魔性沧月",
                true),
            new BookImportResult("book-1", "demo", 1));

        var catalogService = new FakeBookCatalogService([
            new BookSummary("book-1", "demo", null, "第一章 开始", DateTime.UtcNow.ToString("O"))
        ]);

        var viewModel = new LibraryViewModel(importService, catalogService);
        await viewModel.ImportFileAsync("C:\\books\\demo.txt", CancellationToken.None);

        await viewModel.ConfirmImportCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Books);
        Assert.Equal("demo", viewModel.Books[0].Title);
        Assert.Equal("导入成功：demo", viewModel.StatusMessage);
        Assert.Equal(1, importService.CommitCallCount);
        Assert.False(viewModel.CanConfirmImport);
    }

    [Fact]
    public async Task RetryWithEncodingAsync_uses_selected_encoding_and_updates_progress_text()
    {
        var importService = new FakeBookImportService(
            new BookImportAnalysis(
                BookImportAnalysisStatus.ReadyToCommit,
                "C:\\books\\demo.txt",
                "demo.txt",
                "demo",
                "utf-16le",
                "preview",
                "第一章 开始\n正文",
                "hash",
                [new BookImportChapter(0, 0, "第一章 开始", 6, 2)],
                null,
                null,
                null,
                false),
            new BookImportResult("book-1", "demo", 1));

        var viewModel = new LibraryViewModel(importService, new FakeBookCatalogService([]));
        await viewModel.ImportFileAsync("C:\\books\\demo.txt", CancellationToken.None);
        viewModel.SelectedEncoding = "utf-16le";

        await viewModel.RetryWithEncodingCommand.ExecuteAsync(null);

        Assert.Equal("utf-16le", importService.Requests.Last().EncodingOverride);
        Assert.Equal("预览已准备好，可以确认导入。", viewModel.ImportProgressText);
        Assert.False(viewModel.IsFileNameTemplateMatched);
        Assert.Equal("demo", viewModel.SuggestedTitle);
        Assert.Null(viewModel.SuggestedAuthor);
    }

    private sealed class FakeBookImportService : IBookImportService
    {
        private readonly BookImportAnalysis _analysis;
        private readonly BookImportResult _result;
        public List<BookImportRequest> Requests { get; } = [];
        public int CommitCallCount { get; private set; }

        public FakeBookImportService(BookImportAnalysis analysis, BookImportResult result)
        {
            _analysis = analysis;
            _result = result;
        }

        public Task<BookImportAnalysis> AnalyzeAsync(
            BookImportRequest request,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            progress?.Report(new BookImportProgress(BookImportPhase.DetectingEncoding, 100, 100, false, "正在读取小说内容。"));
            return Task.FromResult(_analysis);
        }

        public Task<BookImportResult> CommitAsync(
            BookImportAnalysis analysis,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            CommitCallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeBookCatalogService : IBookCatalogService
    {
        private readonly IReadOnlyList<BookSummary> _books;

        public FakeBookCatalogService(IReadOnlyList<BookSummary> books)
        {
            _books = books;
        }

        public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_books);
        }

        public Task<ContinueListeningSummary?> GetContinueListeningAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<ContinueListeningSummary?>(null);
        }
    }
}
