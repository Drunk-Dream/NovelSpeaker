namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads TXT files with automatic encoding detection and preview generation.
/// </summary>
public interface ITextFileAnalyzer
{
    Task<TextFileAnalysis> AnalyzeAsync(
        BookImportRequest request,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken);
}
