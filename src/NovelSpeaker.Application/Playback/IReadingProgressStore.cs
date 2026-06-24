namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Persists playback progress updates. Full recovery logic is implemented in a later epic.
/// </summary>
public interface IReadingProgressStore
{
    Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken);
}
