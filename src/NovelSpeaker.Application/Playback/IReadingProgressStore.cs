namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Persists playback progress updates used by playback pause, stop, and recovery flows.
/// </summary>
public interface IReadingProgressStore
{
    Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken);

    Task<ReadingProgressEntry?> GetAsync(string bookId, CancellationToken cancellationToken);

    Task<ReadingProgressEntry?> GetMostRecentAsync(CancellationToken cancellationToken);
}
