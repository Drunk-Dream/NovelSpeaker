namespace NovelSpeaker.Application.Books;

/// <summary>
/// Imports a TXT file directly or requests a manual encoding selection when auto-detection is not trusted.
/// </summary>
public interface IDirectBookImportService
{
    Task<DirectBookImportResult> ImportAsync(
        DirectBookImportRequest request,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken);
}
