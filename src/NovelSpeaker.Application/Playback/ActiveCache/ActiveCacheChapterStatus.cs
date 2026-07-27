namespace NovelSpeaker.Application.Playback.ActiveCache;

/// <summary>
/// Describes the current result for one chapter in an active-cache batch.
/// </summary>
public enum ActiveCacheChapterStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed
}
