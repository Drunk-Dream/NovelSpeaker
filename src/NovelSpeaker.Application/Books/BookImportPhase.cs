namespace NovelSpeaker.Application.Books;

/// <summary>
/// Identifies the current phase of a book import operation.
/// </summary>
public enum BookImportPhase
{
    DetectingEncoding,
    HashingContent,
    SplittingChapters,
    CopyingSourceFile,
    SavingBook,
    Completed
}
