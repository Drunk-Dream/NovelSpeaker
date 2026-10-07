namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Immutable, coalesced cache invalidations delivered to active consumers.
/// </summary>
public sealed record CacheInvalidationBatch
{
    public CacheInvalidationBatch(IReadOnlyList<CacheInvalidation> changes, long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Changes = Array.AsReadOnly(changes.ToArray());
        Revision = revision;
    }

    public IReadOnlyList<CacheInvalidation> Changes { get; }

    public long Revision { get; }
}
