namespace NovelSpeaker.Application.Cache;

/// <summary>Accepts facts after a physical cache mutation commits. Composition stays inside Cache.</summary>
public interface ICacheInvalidationSink
{
    void Publish(CacheInvalidation invalidation);
}
