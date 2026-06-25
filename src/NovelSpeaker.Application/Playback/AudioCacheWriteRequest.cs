namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes one generated audio file that should be persisted into the shared audio cache.
/// </summary>
public sealed record AudioCacheWriteRequest(
    AudioCacheKey Key,
    string BookId,
    int ChapterIndex,
    int SegmentIndex,
    long RuleId,
    string SourceFilePath,
    string? ContentType,
    long? DurationMilliseconds = null);
