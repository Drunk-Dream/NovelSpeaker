namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads TXT files with automatic encoding detection and preview generation.
/// </summary>
public interface ITextFileAnalyzer
{
    Task<TextFileAnalysis> AnalyzeAsync(
        string filePath,
        string? encodingName,
        CancellationToken cancellationToken);
}
