using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.Features.Library;

public sealed class LibraryImportCoordinator : ILibraryImportCoordinator
{
    private const long LargeFileThresholdBytes = 5L * 1024 * 1024;

    private readonly IDirectBookImportService _directBookImportService;
    private readonly IEncodingSelectionDialogService _encodingSelectionDialogService;
    private readonly IImportProgressDialogService _importProgressDialogService;
    private readonly IUserDocumentFileOperations _fileOperations;

    public LibraryImportCoordinator(
        IDirectBookImportService directBookImportService,
        IEncodingSelectionDialogService encodingSelectionDialogService,
        IImportProgressDialogService importProgressDialogService,
        IUserDocumentFileOperations fileOperations)
    {
        _directBookImportService = directBookImportService;
        _encodingSelectionDialogService = encodingSelectionDialogService;
        _importProgressDialogService = importProgressDialogService;
        _fileOperations = fileOperations;
    }

    public async Task<LibraryImportCoordinatorResult> ImportAsync(
        string filePath,
        IProgress<BookImportProgress>? inlineProgress,
        CancellationToken cancellationToken)
    {
        var metadata = await _fileOperations.GetMetadataAsync(filePath, cancellationToken);
        if (metadata is null ||
            !string.Equals(metadata.Extension, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.InvalidSource);
        }

        return metadata.Length >= LargeFileThresholdBytes
            ? await _importProgressDialogService.RunAsync(
                metadata.FileName,
                (progress, token) => ImportWithEncodingLoopAsync(
                    metadata.FilePath,
                    metadata.FileName,
                    progress,
                    token),
                cancellationToken)
            : await ImportWithEncodingLoopAsync(
                metadata.FilePath,
                metadata.FileName,
                inlineProgress,
                cancellationToken);
    }

    private async Task<LibraryImportCoordinatorResult> ImportWithEncodingLoopAsync(
        string filePath,
        string fileName,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? selectedEncoding = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _directBookImportService.ImportAsync(
                new DirectBookImportRequest(filePath, selectedEncoding, fileName),
                progress,
                cancellationToken);

            if (result.Status == DirectBookImportStatus.Imported)
            {
                return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Imported);
            }

            if (result.Status == DirectBookImportStatus.Failed)
            {
                if (result.FailureReason == BookImportFailureReason.UnsupportedEncoding && !string.IsNullOrWhiteSpace(selectedEncoding))
                {
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
