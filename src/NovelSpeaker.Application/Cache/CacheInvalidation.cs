namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Describes which cache read models must be queried again after a committed mutation.
/// It deliberately contains no derived totals or percentages.
/// </summary>
public sealed record CacheInvalidation
{
    public CacheInvalidation(
        CacheInvalidationScope scope,
        CacheInvalidationAspect aspects)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (aspects == CacheInvalidationAspect.None)
        {
            throw new ArgumentOutOfRangeException(nameof(aspects));
        }

        Scope = scope;
        Aspects = aspects;
    }

    public CacheInvalidationScope Scope { get; }

    public CacheInvalidationAspect Aspects { get; init; }

    public static CacheInvalidation ForGlobal(CacheInvalidationAspect aspects) =>
        new(new CacheInvalidationScope.Global(), aspects);

    public static CacheInvalidation ForBook(
        string bookId,
        CacheInvalidationAspect aspects) =>
        new(new CacheInvalidationScope.Book(bookId), aspects);

    public static CacheInvalidation ForChapters(
        string bookId,
        IEnumerable<int> chapterIndices,
        CacheInvalidationAspect aspects) =>
        new(new CacheInvalidationScope.Chapters(bookId, chapterIndices), aspects);
}
