namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Process-scoped owner for Cache-local invalidation coalescing.
/// </summary>
internal interface ICacheInvalidationCoordinator : ICacheInvalidationSink, ICacheChangeLifetime, IAsyncDisposable
{
    long Revision { get; }

    event EventHandler<CacheInvalidationBatch>? BatchPublished;

    /// <summary>
    /// Publishes currently pending changes immediately. Production callers normally let the
    /// coordinator's short coalescing window do this; the explicit drain is also used at shutdown.
    /// </summary>
    Task FlushPendingAsync(CancellationToken cancellationToken);
}
