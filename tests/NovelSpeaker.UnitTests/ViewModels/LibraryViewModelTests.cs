using NovelSpeaker.Application.Books;
using NovelSpeaker.App.ViewModels;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task ImportSelectedFileAsync_refreshes_books_after_successful_commit()
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
                [new BookImportChapter(0, "第一章 开始", "正文", 6, 2)],
                null,
                null),
            new BookImportResult("book-1", "demo", 1));

        var catalogService = new FakeBookCatalogService([
            new BookSummary("book-1", "demo", null, "第一章 开始", DateTime.UtcNow.ToString("O"))
        ]);

        var viewModel = new LibraryViewModel(importService, catalogService);

        await viewModel.ImportFileAsync("C:\\books\\demo.txt", CancellationToken.None);

        Assert.Single(viewModel.Books);
        Assert.Equal("demo", viewModel.Books[0].Title);
        Assert.Equal("导入成功：demo", viewModel.StatusMessage);
    }

    private sealed class FakeBookImportService : IBookImportService
    {
        private readonly BookImportAnalysis _analysis;
        private readonly BookImportResult _result;

        public FakeBookImportService(BookImportAnalysis analysis, BookImportResult result)
        {
            _analysis = analysis;
            _result = result;
        }

        public Task<BookImportAnalysis> AnalyzeAsync(string filePath, string? encodingName, CancellationToken cancellationToken)
        {
            return Task.FromResult(_analysis);
        }

        public Task<BookImportResult> CommitAsync(BookImportAnalysis analysis, CancellationToken cancellationToken)
        {
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
    }
}
