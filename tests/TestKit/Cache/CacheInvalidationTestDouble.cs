using NovelSpeaker.Application.Cache;

namespace NovelSpeaker.TestKit.Cache;

internal sealed class CacheInvalidationTestDouble : ICacheInvalidationCoordinator
{
    private EventHandler<CacheInvalidationBatch>? _batchPublished;
    public long Revision { get; private set; }

    public int InvalidationSubscriptionCount => _batchPublished?.GetInvocationList().Length ?? 0;

    public int SubscriberCount => InvalidationSubscriptionCount;

    public event EventHandler<CacheInvalidationBatch>? BatchPublished
    {
        add => _batchPublished += value;
        remove => _batchPublished -= value;
    }

    public void Publish(CacheInvalidation invalidation)
    {
        Revision++;
        _batchPublished?.Invoke(this, new CacheInvalidationBatch([invalidation], Revision));
    }

    public Task FlushPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
