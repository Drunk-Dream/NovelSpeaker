using NovelSpeaker.Application.Playback.ActiveCache;

namespace NovelSpeaker.App.WpfTests;

internal sealed class FakeActiveCacheCoordinator : IActiveCacheCoordinator
{
    public FakeActiveCacheCoordinator(ActiveCacheSnapshot? snapshot = null)
    {
        CurrentSnapshot = snapshot;
    }

    public ActiveCacheSnapshot? CurrentSnapshot { get; private set; }

    public int CancelCallCount { get; private set; }

    public event EventHandler<ActiveCacheSnapshot>? SnapshotChanged;

    public Task<ActiveCacheStartResult> StartAsync(
        StartActiveCacheRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task CancelAsync(CancellationToken cancellationToken)
    {
        CancelCallCount++;
        return Task.CompletedTask;
    }

    public Task WaitForCurrentBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Publish(ActiveCacheSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }
}
