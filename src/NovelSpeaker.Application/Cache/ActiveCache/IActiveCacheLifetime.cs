namespace NovelSpeaker.Application.Cache.ActiveCache;

/// <summary>Stops admission and cancels/drains process-owned active caching.</summary>
public interface IActiveCacheLifetime
{
    Task StopAsync(CancellationToken cancellationToken);
}
