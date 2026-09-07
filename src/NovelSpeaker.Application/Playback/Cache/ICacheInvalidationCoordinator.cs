namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Process-scoped owner for Cache-local invalidation coalescing.
/// </summary>
public interface ICacheInvalidationCoordinator : IAsyncDisposable
{
    event EventHandler<CacheInvalidationBatch>? BatchPublished;

    void Publish(CacheInvalidation invalidation);

    /// <summary>
    /// Publishes currently pending changes immediately. Production callers normally let the
    /// coordinator's short coalescing window do this; the explicit drain is also used at shutdown.
    /// </summary>
    Task FlushPendingAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
