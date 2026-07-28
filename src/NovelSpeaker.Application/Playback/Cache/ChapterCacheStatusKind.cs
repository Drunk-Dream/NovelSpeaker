namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Explains why a chapter's current-configuration cache coverage is or is not available.
/// </summary>
public enum ChapterCacheStatusKind
{
    Available,
    PlanMissing,
    PlanUnavailable,
    NoPlayableContent,
    ConfigurationUnavailable
}
