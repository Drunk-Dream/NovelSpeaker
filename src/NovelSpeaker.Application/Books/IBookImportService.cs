namespace NovelSpeaker.Application.Books;

/// <summary>
/// Coordinates TXT analysis and transactional import commit for the UI layer.
/// </summary>
public interface IBookImportService
{
    Task<BookImportAnalysis> AnalyzeAsync(
        BookImportRequest request,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken);

    Task<BookImportResult> CommitAsync(
        BookImportAnalysis analysis,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken);
}
