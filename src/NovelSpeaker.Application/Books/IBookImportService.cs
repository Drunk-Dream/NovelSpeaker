namespace NovelSpeaker.Application.Books;

/// <summary>
/// Coordinates TXT analysis and transactional import commit for the UI layer.
/// </summary>
public interface IBookImportService
{
    Task<BookImportAnalysis> AnalyzeAsync(
        string filePath,
        string? encodingName,
        CancellationToken cancellationToken);

    Task<BookImportResult> CommitAsync(
        BookImportAnalysis analysis,
        CancellationToken cancellationToken);
}
