namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the single most recent book position used to populate continue-listening UI.
/// </summary>
public sealed record ContinueListeningSummary(
    string BookId,
    string BookTitle,
    string ChapterTitle,
    string LastPlayedAt,
    int CurrentChapterIndex,
    int TotalChapterCount,
    int RemainingChapterCount,
    double OverallProgress,
    int ChapterIndex,
    int SegmentIndex);
