namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Persists playback progress updates. Full recovery logic is implemented in a later epic.
/// </summary>
public interface IReadingProgressStore
{
    Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken);

    Task<ReadingProgressEntry?> GetAsync(string bookId, CancellationToken cancellationToken);

    Task<ReadingProgressEntry?> GetMostRecentAsync(CancellationToken cancellationToken);
}
