namespace NovelSpeaker.Application.Cache;

public sealed record CacheReadResult<T>(long Revision, T Value);

public sealed record CachedBookCatalog(
    CachedBookSummary? Summary,
    IReadOnlyList<CachedChapterCatalogEntry> Chapters);

public sealed record CacheChapterView(
    int ChapterIndex,
    CachedChapterSummary? Physical,
    ChapterCacheStatus Coverage)
{
    public bool IsExportable => Coverage.Kind == ChapterCacheStatusKind.Available &&
                                Coverage.TotalSegmentCount is > 0 &&
                                Coverage.CachedSegmentCount == Coverage.TotalSegmentCount;
}
