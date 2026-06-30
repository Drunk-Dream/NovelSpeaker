using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Dialogs;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.ViewModels;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class ImportBookDialogViewModelTests
{
    [Fact]
    public async Task InitializeAsync_analyzes_file_and_prepares_preview()
    {
        var importService = new FakeBookImportService();
        importService.AnalyzeResults.Enqueue(Task.FromResult(CreateReadyAnalysis("preview", "utf-8")));
        var viewModel = CreateViewModel(importService);

        await viewModel.InitializeAsync(CreateTempTxtFile(), CancellationToken.None);

        Assert.Equal("preview", viewModel.PreviewText);
        Assert.True(viewModel.CanConfirmImport);
        Assert.Equal("utf-8", viewModel.SelectedEncoding);
        Assert.Equal("utf-8", viewModel.DetectedEncodingText);
    }

    [Fact]
    public async Task RetryPreviewAsync_uses_selected_encoding()
    {
        var importService = new FakeBookImportService();
        importService.AnalyzeResults.Enqueue(Task.FromResult(CreateReadyAnalysis("preview-1", "utf-8")));
        importService.AnalyzeResults.Enqueue(Task.FromResult(CreateReadyAnalysis("preview-2", "gb18030")));
        var viewModel = CreateViewModel(importService);

        await viewModel.InitializeAsync(CreateTempTxtFile(), CancellationToken.None);
        viewModel.SelectedEncoding = "gb18030";
        await viewModel.RetryPreviewCommand.ExecuteAsync(null);

        Assert.Equal("preview-2", viewModel.PreviewText);
        Assert.Equal("gb18030", importService.Requests.Last().EncodingOverride);
    }

    [Fact]
    public async Task ConfirmImportAsync_raises_imported_close_request()
    {
        var importService = new FakeBookImportService();
        importService.AnalyzeResults.Enqueue(Task.FromResult(CreateReadyAnalysis("preview", "utf-8")));
        importService.CommitResult = Task.FromResult(new BookImportResult("book-1", "Demo", 2));
        var viewModel = CreateViewModel(importService);
        ImportBookDialogOutcome? outcome = null;
        viewModel.CloseRequested += requestedOutcome => outcome = requestedOutcome;

        await viewModel.InitializeAsync(CreateTempTxtFile(), CancellationToken.None);
        await viewModel.ConfirmImportCommand.ExecuteAsync(null);

        Assert.Equal(ImportBookDialogOutcome.Imported, outcome);
        Assert.Equal(1, importService.CommitCallCount);
    }

    [Fact]
    public async Task InitializeAsync_failure_keeps_dialog_open_and_shows_user_message()
    {
        var importService = new FakeBookImportService();
        importService.AnalyzeResults.Enqueue(Task.FromResult(new BookImportAnalysis(
            BookImportAnalysisStatus.Failed,
            "C:\\books\\demo.txt",
            "demo.txt",
            "demo",
            "unknown",
            string.Empty,
            string.Empty,
            "hash",
            [],
            BookImportFailureReason.UnsupportedEncoding,
            null,
            null,
            false)));
        var viewModel = CreateViewModel(importService);
        var closed = false;
        viewModel.CloseRequested += _ => closed = true;

        await viewModel.InitializeAsync(CreateTempTxtFile(), CancellationToken.None);

        Assert.False(closed);
        Assert.False(viewModel.CanConfirmImport);
        Assert.Equal("自动识别编码失败，请切换编码并重新预览。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RetryPreviewAsync_ignores_late_results_from_previous_analysis()
    {
        var importService = new FakeBookImportService();
        var firstAnalyze = new TaskCompletionSource<BookImportAnalysis>();
        importService.AnalyzeResults.Enqueue(firstAnalyze.Task);
        importService.AnalyzeResults.Enqueue(Task.FromResult(CreateReadyAnalysis("preview-2", "gb18030")));
        var viewModel = CreateViewModel(importService);

        var initializeTask = viewModel.InitializeAsync(CreateTempTxtFile(), CancellationToken.None);
        await WaitForAsync(() => importService.Requests.Count == 1);
        viewModel.SelectedEncoding = "gb18030";
        await viewModel.RetryPreviewCommand.ExecuteAsync(null);

        firstAnalyze.SetResult(CreateReadyAnalysis("stale-preview", "utf-8"));
        await initializeTask;

        Assert.Equal("preview-2", viewModel.PreviewText);
        Assert.Equal("gb18030", viewModel.SelectedEncoding);
    }

    private static ImportBookDialogViewModel CreateViewModel(FakeBookImportService importService)
    {
        return new ImportBookDialogViewModel(importService, new FakeFeedbackService());
    }

    private static BookImportAnalysis CreateReadyAnalysis(string previewText, string detectedEncoding)
    {
        return new BookImportAnalysis(
            BookImportAnalysisStatus.ReadyToCommit,
            "C:\\books\\demo.txt",
            "demo.txt",
            "demo",
            detectedEncoding,
            previewText,
            "第一章 开始\n正文",
            "hash",
            [new BookImportChapter(0, 0, "第一章 开始", 6, 2)],
            null,
            null,
            "魔性沧月",
            true);
    }

    private static string CreateTempTxtFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(filePath, "demo");
        return filePath;
    }

    private sealed class FakeBookImportService : IBookImportService
    {
        public Queue<Task<BookImportAnalysis>> AnalyzeResults { get; } = new();

        public List<BookImportRequest> Requests { get; } = [];

        public Task<BookImportResult> CommitResult { get; set; } = Task.FromResult(new BookImportResult("book-1", "Demo", 1));

        public int CommitCallCount { get; private set; }

        public async Task<BookImportAnalysis> AnalyzeAsync(
            BookImportRequest request,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            progress?.Report(new BookImportProgress(BookImportPhase.DetectingEncoding, 100, 100, false, "正在读取小说内容。"));
            return await AnalyzeResults.Dequeue().WaitAsync(cancellationToken);
        }

        public Task<BookImportResult> CommitAsync(
            BookImportAnalysis analysis,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            CommitCallCount++;
            return CommitResult;
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception)
        {
            return new ExceptionProjector().Project(exception);
        }

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Cancel);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - startedAt > TimeSpan.FromSeconds(1))
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10);
        }
    }
}
