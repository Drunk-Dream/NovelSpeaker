namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Registers speech-plan repairs discovered by a read-only cache coverage projection.
/// </summary>
public interface ICachePlanRepairRequestor
{
    Task RequestAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        IReadOnlyCollection<ChapterCacheStatus> statuses,
        CancellationToken cancellationToken);
}
