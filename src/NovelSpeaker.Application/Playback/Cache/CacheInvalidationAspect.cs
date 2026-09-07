namespace NovelSpeaker.Application.Playback.Cache;

[Flags]
public enum CacheInvalidationAspect
{
    None = 0,
    PhysicalSummary = 1,
    CatalogStructure = 2,
    Coverage = 4
}
