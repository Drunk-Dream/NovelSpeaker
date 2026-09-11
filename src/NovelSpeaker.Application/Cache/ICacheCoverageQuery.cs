using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Reads current-configuration cache coverage for explicitly requested chapters.
/// </summary>
public interface ICacheCoverageQuery
{
    Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken);

    /// <summary>
    /// Evaluates the same query using metadata already loaded by a catalog projection.
    /// </summary>
    Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        IReadOnlyCollection<PlaybackChapterMetadata> chapters,
        CancellationToken cancellationToken);
}
