using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.TestKit.Cache;

internal sealed class CacheCoverageTestDouble : ICacheCoverageQuery
{
    public IReadOnlyList<ChapterCacheStatus> Statuses { get; set; } = [];

    public Func<
        string,
        IReadOnlyCollection<int>,
        CancellationToken,
        Task<IReadOnlyList<ChapterCacheStatus>>>?
        CoverageHandler { get; set; }

    public int CoverageQueryCallCount { get; private set; }

    public int StatusCallCount => CoverageQueryCallCount;

    public IReadOnlyList<int> LastRequestedChapterIndices { get; private set; } = [];

    public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        CoverageQueryCallCount++;
        LastRequestedChapterIndices = chapterIndices.ToArray();
        if (CoverageHandler is not null)
        {
            return CoverageHandler(bookId, chapterIndices, cancellationToken);
        }

        return Task.FromResult<IReadOnlyList<ChapterCacheStatus>>(
            Statuses.Where(status => chapterIndices.Contains(status.ChapterIndex)).ToArray());
    }

    public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        IReadOnlyCollection<PlaybackChapterMetadata> chapters,
        CancellationToken cancellationToken) =>
        GetAsync(bookId, chapterIndices, cancellationToken);
}
