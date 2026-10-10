using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.Books.Library;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Library;

public sealed class LibraryImportCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_closes_progress_before_metadata_confirmation_and_resumes_or_cancels(bool cancel)
    {
        var service = new FakeDirectBookImportService();
        var defaults = new BookImportIdentity("Fixture", "");
        service.Results.Enqueue(new DirectBookImportResult(DirectBookImportStatus.RequiresMetadataConfirmation,
            MetadataConfirmation: defaults));
        service.Results.Enqueue(new DirectBookImportResult(DirectBookImportStatus.Imported));
        var progress = new FakeImportProgressDialogService();
        var selection = new FakeMetadataDialog
        {
            Selection = cancel ? null : new BookImportIdentity("Corrected", "Author"),
            OnShow = () => Assert.False(progress.IsOpen)
        };
        var coordinator = new LibraryImportCoordinator(service, new FakeEncodingSelectionDialogService(), progress,
            FakeUserDocumentFileOperations.ForFile("demo.txt", 6 * 1024 * 1024), selection);
        var result = await coordinator.ImportAsync("demo.txt", CancellationToken.None);
        Assert.Equal(defaults, selection.Defaults);
        if (cancel)
        {
            Assert.Equal(LibraryImportCoordinatorStatus.Cancelled, result.Status);
            Assert.Single(service.Requests);
        }
        else
        {
            Assert.Equal(LibraryImportCoordinatorStatus.Imported, result.Status);
            Assert.Equal(selection.Selection, service.Requests[1].ConfirmedIdentity);
        }
    }

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
            DirectBookImportStatus.RequiresMetadataConfirmation,
            MetadataConfirmation: new("Demo", "")));
        directImportService.Results.Enqueue(new DirectBookImportResult(
            DirectBookImportStatus.Imported,
            ImportedBook: new BookImportResult("book-1", "Demo", 3)));

        var encodingDialog = new FakeEncodingSelectionDialogService
        {
            NextEncoding = "utf-8"
        };
        var metadataDialog = new FakeMetadataDialog
        {
            Selection = new("Corrected", "Author"),
            OnShow = () => Assert.Equal("utf-8", directImportService.Requests[^1].EncodingOverride)
        };
        var coordinator = new LibraryImportCoordinator(
            directImportService,
            encodingDialog,
            new FakeImportProgressDialogService(),
            FakeUserDocumentFileOperations.ForFile("demo.txt", 256), metadataDialog);

        var result = await coordinator.ImportAsync("demo.txt", CancellationToken.None);

        Assert.Equal(LibraryImportCoordinatorStatus.Imported, result.Status);
        Assert.Equal([null, "utf-8", "utf-8"], directImportService.Requests.Select(static request => request.EncodingOverride));
        Assert.Null(directImportService.Requests[1].ConfirmedIdentity);
        Assert.Equal(metadataDialog.Selection, directImportService.Requests[2].ConfirmedIdentity);
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
            new FakeImportProgressDialogService(),
            FakeUserDocumentFileOperations.ForFile("demo.txt", 256), new FakeMetadataDialog());

        var result = await coordinator.ImportAsync("demo.txt", CancellationToken.None);

        Assert.Equal(LibraryImportCoordinatorStatus.Cancelled, result.Status);
        Assert.Single(directImportService.Requests);
    }

    [Theory]
    [InlineData(5 * 1024 * 1024 - 1, false)]
    [InlineData(5 * 1024 * 1024, true)]
    [InlineData(6 * 1024 * 1024, true)]
    public async Task ImportAsync_uses_progress_dialog_only_for_files_at_or_above_five_mib(long fileSize, bool showsProgressDialog)
    {
        var directImportService = new FakeDirectBookImportService();
        directImportService.Results.Enqueue(new DirectBookImportResult(
            DirectBookImportStatus.Imported,
            ImportedBook: new BookImportResult("book-1", "Demo", 1)));
        var progressDialog = new FakeImportProgressDialogService();
        var coordinator = new LibraryImportCoordinator(
            directImportService,
            new FakeEncodingSelectionDialogService(),
            progressDialog,
            FakeUserDocumentFileOperations.ForFile("demo.txt", fileSize), new FakeMetadataDialog());

        var result = await coordinator.ImportAsync("demo.txt", CancellationToken.None);

        Assert.Equal(LibraryImportCoordinatorStatus.Imported, result.Status);
        Assert.Equal(showsProgressDialog, progressDialog.WasInvoked);
        Assert.Equal(showsProgressDialog ? "demo.txt" : null, progressDialog.FileName);
    }

    [Fact]
    public async Task ImportAsync_rejects_missing_or_non_txt_sources()
    {
        foreach (var (filePath, extension) in new[]
                 {
                     ("missing.txt", (string?)null),
                     ("novel.epub", ".epub")
                 })
        {
            var directImportService = new FakeDirectBookImportService();
            var fileOperations = extension is null
                ? new FakeUserDocumentFileOperations()
                : FakeUserDocumentFileOperations.ForFile(filePath, 256, extension);
            var coordinator = new LibraryImportCoordinator(
                directImportService,
                new FakeEncodingSelectionDialogService(),
                new FakeImportProgressDialogService(),
                fileOperations, new FakeMetadataDialog());

            var result = await coordinator.ImportAsync(filePath, CancellationToken.None);

            Assert.Equal(LibraryImportCoordinatorStatus.InvalidSource, result.Status);
            Assert.Empty(directImportService.Requests);
        }
    }

    [Fact]
    public async Task ImportAsync_propagates_metadata_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var coordinator = new LibraryImportCoordinator(
            new FakeDirectBookImportService(),
            new FakeEncodingSelectionDialogService(),
            new FakeImportProgressDialogService(),
            FakeUserDocumentFileOperations.ForFile("demo.txt", 256), new FakeMetadataDialog());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.ImportAsync("demo.txt", cancellation.Token));
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

    private sealed class FakeMetadataDialog : IBookImportMetadataDialogService
    {
        public BookImportIdentity? Selection { get; init; }
        public Action? OnShow { get; init; }
        public BookImportIdentity? Defaults { get; private set; }
        public Task<BookImportIdentity?> ShowAsync(BookImportIdentity defaults, CancellationToken cancellationToken)
        {
            Defaults = defaults;
            OnShow?.Invoke();
            return Task.FromResult(Selection);
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

        public string? FileName { get; private set; }
        public bool IsOpen { get; private set; }

        public async Task<LibraryImportCoordinatorResult> RunAsync(
            string fileName,
            Func<IProgress<BookImportProgress>, CancellationToken, Task<LibraryImportCoordinatorResult>> operation,
            CancellationToken cancellationToken)
        {
            WasInvoked = true;
            FileName = fileName;
            IsOpen = true;
            try
            {
                return await operation(new Progress<BookImportProgress>(), cancellationToken);
            }
            finally
            {
                IsOpen = false;
            }
        }
    }

    private sealed class FakeUserDocumentFileOperations : IUserDocumentFileOperations
    {
        private UserDocumentFileMetadata? _metadata;

        public static FakeUserDocumentFileOperations ForFile(
            string filePath,
            long length,
            string extension = ".txt")
        {
            return new FakeUserDocumentFileOperations
            {
                _metadata = new UserDocumentFileMetadata(
                    filePath,
                    Path.GetFileName(filePath),
                    extension,
                    length)
            };
        }

        public Task<UserDocumentFileMetadata?> GetMetadataAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_metadata);
        }

        public Task<string> ReadTextAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WriteTextAsync(string filePath, string content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
