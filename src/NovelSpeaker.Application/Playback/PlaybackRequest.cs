namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes a single local audio item that should be loaded into the playback pipeline.
/// </summary>
public sealed record PlaybackRequest(
    string FilePath,
    string DisplayTitle,
    string? BookId,
    int ChapterIndex,
    int SegmentIndex,
    long ResumePositionMilliseconds,
    bool IsUsingCache);
