using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Adapts the cache-page request shape to the process-owned speech-plan repair coordinator.
/// </summary>
internal sealed class SpeechPlanRepairRequestor(
    IBookPlaybackMetadataQuery metadataQuery,
    ISpeechPlanRepairCoordinator coordinator) : ICachePlanRepairRequestor
{
    public async Task RequestAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        IReadOnlyCollection<ChapterCacheStatus> statuses,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);
        ArgumentNullException.ThrowIfNull(statuses);

        var repairIndices = statuses
            .Where(static status => status.Kind is ChapterCacheStatusKind.PlanMissing or ChapterCacheStatusKind.PlanStale)
            .Select(static status => status.ChapterIndex)
            .Intersect(chapterIndices)
            .Distinct()
            .Order()
            .ToArray();
        if (repairIndices.Length == 0)
        {
            return;
        }

        var chapters = await metadataQuery
            .GetChaptersAsync(bookId, repairIndices, cancellationToken)
            .ConfigureAwait(false);
        foreach (var chapter in chapters)
        {
            if (string.IsNullOrWhiteSpace(chapter.ChapterId))
            {
                continue;
            }

            Observe(coordinator.RequestAsync(
                new SpeechPlanRepairRequest(bookId, chapter.ChapterIndex, chapter.ChapterId),
                CancellationToken.None));
        }
    }

    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
