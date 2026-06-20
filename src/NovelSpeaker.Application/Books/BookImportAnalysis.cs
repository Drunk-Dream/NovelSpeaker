namespace NovelSpeaker.Application.Books;

/// <summary>
/// Holds the analysis outcome before the caller decides whether to commit it.
/// </summary>
public sealed record BookImportAnalysis(
    BookImportAnalysisStatus Status,
    string OriginalFilePath,
    string OriginalFileName,
    string SuggestedTitle,
    string DetectedEncoding,
    string PreviewText,
    string NormalizedText,
    string SourceHash,
    IReadOnlyList<BookImportChapter> Chapters,
    BookImportFailureReason? FailureReason,
    string? ExistingBookId);
