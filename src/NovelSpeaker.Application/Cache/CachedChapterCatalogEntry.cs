namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Immutable cache chapter identity used to build a large presentation catalog.
/// Dynamic cache coverage is queried separately for the active decoration window.
/// </summary>
public sealed record CachedChapterCatalogEntry(
    string BookId,
    int ChapterIndex,
    string Title);
