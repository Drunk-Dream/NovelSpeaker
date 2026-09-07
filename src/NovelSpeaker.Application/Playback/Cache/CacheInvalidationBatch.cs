namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Immutable, coalesced cache invalidations delivered to active consumers.
/// </summary>
public sealed record CacheInvalidationBatch
{
    public CacheInvalidationBatch(IReadOnlyList<CacheInvalidation> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Changes = Array.AsReadOnly(changes.ToArray());
    }

    public IReadOnlyList<CacheInvalidation> Changes { get; }
}
