using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Placeholder progress store used until reading progress persistence is implemented.
/// </summary>
public sealed class NoOpReadingProgressStore : IReadingProgressStore
{
    public Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<ReadingProgressEntry?> GetAsync(string bookId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ReadingProgressEntry?>(null);
    }

    public Task<ReadingProgressEntry?> GetMostRecentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ReadingProgressEntry?>(null);
    }
}
