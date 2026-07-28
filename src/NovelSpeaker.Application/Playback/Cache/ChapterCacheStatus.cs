namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Describes audio coverage for one chapter under the current playback configuration.
/// <see cref="Kind"/> explains why a null total is unavailable or why a zero total is valid.
/// </summary>
public sealed record ChapterCacheStatus(
    int ChapterIndex,
    int CachedSegmentCount,
    int? TotalSegmentCount)
{
    /// <summary>
    /// Distinguishes an unavailable calculation from a valid zero-percent result.
    /// </summary>
    public ChapterCacheStatusKind Kind { get; init; } = TotalSegmentCount is null
        ? ChapterCacheStatusKind.ConfigurationUnavailable
        : ChapterCacheStatusKind.Available;
}
