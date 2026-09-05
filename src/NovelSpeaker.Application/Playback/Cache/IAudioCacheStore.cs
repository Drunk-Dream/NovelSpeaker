namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Persists and queries the audio cache without exposing storage technology to application use cases.
/// </summary>
public interface IAudioCacheStore
{
    event EventHandler<CacheChangedEventArgs>? Changed;

    Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken);

    Task<CachedBookStoreSummary?> GetBookAsync(
        string bookId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
        string bookId,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chapterIndices);
        var chapters = new List<CachedChapterStoreSummary>(chapterIndices.Count);
        foreach (var chapterIndex in chapterIndices.Distinct().Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await GetChapterAsync(bookId, chapterIndex, cancellationToken).ConfigureAwait(false) is { } chapter)
            {
                chapters.Add(chapter);
            }
        }

        return chapters;
    }

    Task<CachedChapterStoreSummary?> GetChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken);

    /// <summary>
    /// Aggregates current plan coverage from persisted plan metadata and Ready cache index rows.
    /// This query must not inspect cache files, decode audio, or update LRU metadata.
    /// </summary>
    Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
        IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
        SynthesisProfileFingerprint synthesisProfile,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the requested entries that have indexed, decodable cache files during this read-only
    /// snapshot evaluation, without updating LRU metadata. Concurrent mutations may make the returned
    /// snapshot immediately stale; callers use <see cref="Changed"/> to schedule a refresh.
    /// </summary>
    Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
        IReadOnlyCollection<AudioCacheKey> keys,
        CancellationToken cancellationToken);

    Task<AudioCacheStoreCleanupResult> ClearChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken);

    Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken);

    Task<AudioCacheStoreCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken);

    Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken);

    Task RunMaintenanceAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs the startup-only cache maintenance pass, including cleanup of plans without
    /// any persisted cache index entry. This must complete before interactive playback
    /// or cache work can create a new plan.
    /// </summary>
    Task RunStartupMaintenanceAsync(CancellationToken cancellationToken);
}
