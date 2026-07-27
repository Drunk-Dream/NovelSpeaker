namespace NovelSpeaker.Application.Playback.ActiveCache;

/// <summary>
/// Owns the single process-wide active-cache batch independently of any page or playback session.
/// </summary>
public interface IActiveCacheCoordinator
{
    ActiveCacheSnapshot? CurrentSnapshot { get; }

    event EventHandler<ActiveCacheSnapshot>? SnapshotChanged;

    Task<ActiveCacheStartResult> StartAsync(
        StartActiveCacheRequest request,
        CancellationToken cancellationToken);

    Task CancelAsync(CancellationToken cancellationToken);

    Task WaitForCurrentBatchAsync(CancellationToken cancellationToken);
}
