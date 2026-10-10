using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.Features.Books.Library;

public sealed class LibraryImportCoordinator : ILibraryImportCoordinator
{
    private const long LargeFileThresholdBytes = 5L * 1024 * 1024;

    private readonly IDirectBookImportService _directBookImportService;
    private readonly IEncodingSelectionDialogService _encodingSelectionDialogService;
    private readonly IImportProgressDialogService _importProgressDialogService;
    private readonly IUserDocumentFileOperations _fileOperations;
    private readonly IBookImportMetadataDialogService _metadataDialogService;

    public LibraryImportCoordinator(
        IDirectBookImportService directBookImportService,
        IEncodingSelectionDialogService encodingSelectionDialogService,
        IImportProgressDialogService importProgressDialogService,
        IUserDocumentFileOperations fileOperations,
        IBookImportMetadataDialogService metadataDialogService)
    {
        _directBookImportService = directBookImportService;
        _encodingSelectionDialogService = encodingSelectionDialogService;
        _importProgressDialogService = importProgressDialogService;
        _fileOperations = fileOperations;
        _metadataDialogService = metadataDialogService;
    }

    public async Task<LibraryImportCoordinatorResult> ImportAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var metadata = await _fileOperations.GetMetadataAsync(filePath, cancellationToken);
        if (metadata is null ||
            !string.Equals(metadata.Extension, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.InvalidSource);
        }

        return await ImportWithSelectionLoopAsync(metadata, cancellationToken);
    }

    private async Task<LibraryImportCoordinatorResult> ImportWithSelectionLoopAsync(
        UserDocumentFileMetadata metadata,
        CancellationToken cancellationToken)
    {
        string? selectedEncoding = null;
        BookImportIdentity? confirmedIdentity = null;
        var filePath = metadata.FilePath;
        var fileName = metadata.FileName;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            async Task<LibraryImportCoordinatorResult> AttemptAsync(IProgress<BookImportProgress>? progress, CancellationToken token)
            {
                var attempt = await _directBookImportService.ImportAsync(
                    new DirectBookImportRequest(filePath, selectedEncoding, fileName, confirmedIdentity),
                    progress, token);
                return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.RequiresInput, PendingImport: attempt);
            }

            // Every attempt closes its progress surface before showing a selection surface.
            var attemptResult = metadata.Length >= LargeFileThresholdBytes
                ? await _importProgressDialogService.RunAsync(fileName, (progress, token) => AttemptAsync(progress, token), cancellationToken)
                : await AttemptAsync(null, cancellationToken);
            if (attemptResult.Status == LibraryImportCoordinatorStatus.Cancelled)
            {
                return attemptResult;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = attemptResult.PendingImport!;

            if (result.Status == DirectBookImportStatus.Imported)
            {
                return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Imported);
            }

            if (result.Status == DirectBookImportStatus.RequiresMetadataConfirmation)
            {
                confirmedIdentity = await _metadataDialogService.ShowAsync(result.MetadataConfirmation!, cancellationToken);
                if (confirmedIdentity is null)
                {
                    return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Cancelled);
                }

                continue;
            }

            if (result.Status == DirectBookImportStatus.Failed)
            {
                if (result.FailureReason == BookImportFailureReason.UnsupportedEncoding && !string.IsNullOrWhiteSpace(selectedEncoding))
                {
                    confirmedIdentity = null;
                    selectedEncoding = await _encodingSelectionDialogService.ShowAsync(
                        new EncodingSelectionPrompt(
                            filePath,
                            fileName,
                            "所选编码无法读取该文件，请重新选择后继续导入。",
                            selectedEncoding,
                            ["utf-8", "utf-16le", "utf-16be", "gb18030"]),
                        cancellationToken);
                    if (selectedEncoding is null)
                    {
                        return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Cancelled);
                    }

                    continue;
                }

                return new LibraryImportCoordinatorResult(
                    LibraryImportCoordinatorStatus.Failed,
                    result.FailureReason);
            }

            confirmedIdentity = null;
            selectedEncoding = await _encodingSelectionDialogService.ShowAsync(
                result.EncodingSelectionPrompt!,
                cancellationToken);
            if (selectedEncoding is null)
            {
                return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Cancelled);
            }
        }
    }
}
