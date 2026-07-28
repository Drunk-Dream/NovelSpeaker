namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Describes one generated audio file that should be persisted into the shared audio cache.
/// </summary>
public sealed record AudioCacheWriteRequest(
    AudioCacheKey Key,
    string BookId,
    int ChapterIndex,
    long RuleId,
    string SourceFilePath,
    string? ContentType,
    long? DurationMilliseconds = null);
