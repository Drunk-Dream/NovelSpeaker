namespace NovelSpeaker.Application.Playback.ActiveCache;

/// <summary>
/// Describes the process-owned lifecycle of one active-cache batch.
/// </summary>
public enum ActiveCacheBatchStatus
{
    Waiting,
    Running,
    Cancelling,
    Completed,
    Cancelled,
    Failed
}
