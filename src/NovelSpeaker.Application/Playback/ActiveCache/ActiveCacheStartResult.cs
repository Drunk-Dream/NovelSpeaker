namespace NovelSpeaker.Application.Playback.ActiveCache;

/// <summary>
/// Returns the stable outcome of attempting to reserve the application-wide active batch slot.
/// </summary>
public sealed record ActiveCacheStartResult(
    ActiveCacheStartStatus Status,
    Guid? BatchId,
    string? ErrorSummary)
{
    public bool IsAccepted => Status == ActiveCacheStartStatus.Accepted;
}
