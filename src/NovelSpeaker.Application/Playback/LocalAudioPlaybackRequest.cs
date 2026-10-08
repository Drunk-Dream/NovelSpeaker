namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes a single local audio file that should be loaded into the low-level playback pipeline.
/// </summary>
public sealed record LocalAudioPlaybackRequest(
    string FilePath,
    string DisplayTitle,
    string? BookId,
    int ChapterIndex,
    int SegmentIndex,
    long ResumePositionMilliseconds,
    bool IsUsingCache,
    Guid PlaybackSessionId,
    long TargetRevision = 0,
    Guid? PreparationAttemptId = null);
