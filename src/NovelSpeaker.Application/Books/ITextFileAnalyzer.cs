namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads TXT files with automatic encoding detection and low-confidence metadata.
/// </summary>
public interface ITextFileAnalyzer
{
    Task<TextFileAnalysis> AnalyzeAsync(
        BookImportRequest request,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken);
}
