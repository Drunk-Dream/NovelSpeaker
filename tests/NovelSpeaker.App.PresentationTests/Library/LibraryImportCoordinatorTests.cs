using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.Library;
using Xunit;

namespace NovelSpeaker.UnitTests.Library;

public sealed class LibraryImportCoordinatorTests
{
    [Fact]
    public async Task ImportAsync_prompts_for_encoding_and_retries_until_import_succeeds()
    {
        var directImportService = new FakeDirectBookImportService();
        directImportService.Results.Enqueue(new DirectBookImportResult(
            DirectBookImportStatus.RequiresEncodingSelection,
            EncodingSelectionPrompt: new EncodingSelectionPrompt(
                "demo.txt",
                "demo.txt",
                "请选择编码。",
                "gb18030",
                ["utf-8", "utf-16le", "utf-16be", "gb18030"])));
        directImportService.Results.Enqueue(new DirectBookImportResult(
            DirectBookImportStatus.Imported,
            ImportedBook: new BookImportResult("book-1", "Demo", 3)));

        var encodingDialog = new FakeEncodingSelectionDialogService
        {
            NextEncoding = "utf-8"
        };
        var coordinator = new LibraryImportCoordinator(
            directImportService,
            encodingDialog,
            new FakeImportProgressDialogService());

        var result = await coordinator.ImportAsync(CreateTempTxtFile(256), inlineProgress: null, CancellationToken.None);

        Assert.Equal(LibraryImportCoordinatorStatus.Imported, result.Status);
        Assert.Equal([null, "utf-8"], directImportService.Requests.Select(static request => request.EncodingOverride));
    }

    [Fact]
    public async Task ImportAsync_returns_cancelled_when_encoding_selection_is_cancelled()
    {
        var directImportService = new FakeDirectBookImportService();
        directImportService.Results.Enqueue(new DirectBookImportResult(
            DirectBookImportStatus.RequiresEncodingSelection,
            EncodingSelectionPrompt: new EncodingSelectionPrompt(
                "demo.txt",
                "demo.txt",
                "请选择编码。",
                "gb18030",
                ["utf-8", "utf-16le", "utf-16be", "gb18030"])));

        var coordinator = new LibraryImportCoordinator(
            directImportService,
            new FakeEncodingSelectionDialogService(),
            new FakeImportProgressDialogService());

        var result = await coordinator.ImportAsync(CreateTempTxtFile(256), inlineProgress: null, CancellationToken.None);

        Assert.Equal(LibraryImportCoordinatorStatus.Cancelled, result.Status);
        Assert.Single(directImportService.Requests);
    }

    [Fact]
    public async Task ImportAsync_uses_progress_dialog_for_large_files()
    {
        var directImportService = new FakeDirectBookImportService();
        directImportService.Results.Enqueue(new DirectBookImportResult(
            DirectBookImportStatus.Imported,
            ImportedBook: new BookImportResult("book-1", "Demo", 1)));
        var progressDialog = new FakeImportProgressDialogService();
        var coordinator = new LibraryImportCoordinator(
            directImportService,
            new FakeEncodingSelectionDialogService(),
            progressDialog);

        var result = await coordinator.ImportAsync(CreateTempTxtFile(6 * 1024 * 1024), inlineProgress: null, CancellationToken.None);

        Assert.Equal(LibraryImportCoordinatorStatus.Imported, result.Status);
        Assert.True(progressDialog.WasInvoked);
    }

    private static string CreateTempTxtFile(int sizeInBytes)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(filePath, Enumerable.Repeat((byte)'a', sizeInBytes).ToArray());
        return filePath;
    }

    private sealed class FakeDirectBookImportService : IDirectBookImportService
    {
        public Queue<DirectBookImportResult> Results { get; } = new();

        public List<DirectBookImportRequest> Requests { get; } = [];

        public Task<DirectBookImportResult> ImportAsync(
            DirectBookImportRequest request,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Results.Dequeue());
        }
    }

    private sealed class FakeEncodingSelectionDialogService : IEncodingSelectionDialogService
    {
        public string? NextEncoding { get; set; }

        public Task<string?> ShowAsync(EncodingSelectionPrompt prompt, CancellationToken cancellationToken)
        {
            return Task.FromResult(NextEncoding);
        }
    }

    private sealed class FakeImportProgressDialogService : IImportProgressDialogService
    {
        public bool WasInvoked { get; private set; }

        public Task<LibraryImportCoordinatorResult> RunAsync(
            string fileName,
            Func<IProgress<BookImportProgress>, CancellationToken, Task<LibraryImportCoordinatorResult>> operation,
            CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return operation(new Progress<BookImportProgress>(), cancellationToken);
        }
    }
}
