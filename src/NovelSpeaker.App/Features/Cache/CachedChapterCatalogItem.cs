using NovelSpeaker.Application.Playback.Cache;

namespace NovelSpeaker.App.Features.Cache;

/// <summary>
/// Immutable chapter identity used by the cache page catalog. Cache counts and completeness
/// are mutable decorations and must not be retained in this catalog.
/// </summary>
internal sealed record CachedChapterCatalogItem(
    string BookId,
    string Title,
    int ChapterIndex)
{
    public static CachedChapterCatalogItem From(CachedChapterCatalogEntry chapter) =>
        new(
            chapter.BookId,
            chapter.Title,
            chapter.ChapterIndex);
}

/// <summary>
/// A cache row's mutable display data. Initial values are used only while the source catalog
/// is projected; later targeted changes are held sparsely until applied to the row.
/// </summary>
internal sealed record CachedChapterDecoration(
    string Title,
    int CachedSegmentCount,
    int EntryCount,
    long TotalSizeBytes,
    int? CurrentConfigurationSegmentCount,
    ChapterCacheStatusKind CurrentConfigurationStatus)
{
    public static CachedChapterDecoration Placeholder(string title) =>
        new(
            title,
            0,
            0,
            0,
            null,
            ChapterCacheStatusKind.ConfigurationUnavailable);

    public static CachedChapterDecoration From(
        CachedChapterSummary chapter,
        ChapterCacheStatus status) =>
        new(
            chapter.Title,
            status.CachedSegmentCount,
            chapter.EntryCount,
            chapter.TotalSizeBytes,
            status.TotalSegmentCount,
            status.Kind);
}
