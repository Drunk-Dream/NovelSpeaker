namespace NovelSpeaker.Application.Cache;

/// <summary>Application port for the one-current-plan-per-chapter persistence boundary.</summary>
public interface IChapterSpeechPlanStore
{
    Task<ChapterSpeechPlan?> GetAsync(
        string chapterId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        ChapterSpeechPlan plan,
        CancellationToken cancellationToken);

    /// <summary>Saves only while a physical audio index entry still exists, atomically with that check.</summary>
    Task<bool> SaveIfCachedAsync(ChapterSpeechPlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes plans whose chapters no longer have any persisted audio-cache index entry.
    /// </summary>
    Task<int> DeletePlansWithoutCacheEntriesAsync(CancellationToken cancellationToken);
}
