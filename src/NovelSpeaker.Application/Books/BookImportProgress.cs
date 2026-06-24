namespace NovelSpeaker.Application.Books;

/// <summary>
/// Carries user-facing progress updates during analysis and commit.
/// </summary>
public sealed record BookImportProgress(
    BookImportPhase Phase,
    long BytesProcessed,
    long TotalBytes,
    bool IsIndeterminate,
    string Message);
