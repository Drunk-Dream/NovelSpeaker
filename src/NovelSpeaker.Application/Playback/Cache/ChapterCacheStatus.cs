namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Describes valid audio coverage for one chapter under the current playback configuration.
/// A null total means that the current configuration or chapter content is unavailable.
/// </summary>
public sealed record ChapterCacheStatus(
    int ChapterIndex,
    int CachedSegmentCount,
    int? TotalSegmentCount);
