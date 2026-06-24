namespace NovelSpeaker.Application.Books;

/// <summary>
/// Copies imported source files into the application-owned book storage.
/// </summary>
public interface IBookFileStore
{
    Task<BookFileCopyHandle> PrepareCopyAsync(
        string sourceFilePath,
        string bookId,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken);

    Task FinalizeAsync(BookFileCopyHandle copyHandle, CancellationToken cancellationToken);
    Task CleanupAsync(BookFileCopyHandle copyHandle);
}
