using NovelSpeaker.Application.Playback.ActiveCache;

namespace NovelSpeaker.App.WpfTests;

internal sealed class FakeActiveCacheCoordinator : IActiveCacheCoordinator
{
    public ActiveCacheSnapshot? CurrentSnapshot => null;

    public event EventHandler<ActiveCacheSnapshot>? SnapshotChanged
    {
        add { }
        remove { }
    }

    public Task<ActiveCacheStartResult> StartAsync(
        StartActiveCacheRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task CancelAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WaitForCurrentBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
