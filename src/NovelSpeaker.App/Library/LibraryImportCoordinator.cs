using System.IO;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Dialogs;

namespace NovelSpeaker.App.Library;

public sealed class LibraryImportCoordinator : ILibraryImportCoordinator
{
    private const long LargeFileThresholdBytes = 5L * 1024 * 1024;

    private readonly IDirectBookImportService _directBookImportService;
    private readonly IEncodingSelectionDialogService _encodingSelectionDialogService;
    private readonly IImportProgressDialogService _importProgressDialogService;

    public LibraryImportCoordinator(
        IDirectBookImportService directBookImportService,
        IEncodingSelectionDialogService encodingSelectionDialogService,
        IImportProgressDialogService importProgressDialogService)
    {
        _directBookImportService = directBookImportService;
        _encodingSelectionDialogService = encodingSelectionDialogService;
        _importProgressDialogService = importProgressDialogService;
    }

    public Task<LibraryImportCoordinatorResult> ImportAsync(
        string filePath,
        IProgress<BookImportProgress>? inlineProgress,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        return fileInfo.Exists && fileInfo.Length >= LargeFileThresholdBytes
            ? _importProgressDialogService.RunAsync(
                fileInfo.Name,
                (progress, token) => ImportWithEncodingLoopAsync(filePath, progress, token),
                cancellationToken)
            : ImportWithEncodingLoopAsync(filePath, inlineProgress, cancellationToken);
    }

    private async Task<LibraryImportCoordinatorResult> ImportWithEncodingLoopAsync(
        string filePath,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? selectedEncoding = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _directBookImportService.ImportAsync(
                new DirectBookImportRequest(filePath, selectedEncoding),
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
                            Path.GetFileName(filePath),
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
