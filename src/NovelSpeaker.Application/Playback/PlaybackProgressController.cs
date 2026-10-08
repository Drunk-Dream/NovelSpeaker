namespace NovelSpeaker.Application.Playback;

/// <summary>Executes immutable checkpoints from committed runtime transitions.</summary>
internal sealed class PlaybackProgressController
{
    private readonly IReadingProgressStore _readingProgressStore;

    public PlaybackProgressController(IReadingProgressStore readingProgressStore)
    {
        _readingProgressStore = readingProgressStore ?? throw new ArgumentNullException(nameof(readingProgressStore));
    }

    public Task<ReadingProgressEntry?> RestoreAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return _readingProgressStore.GetAsync(bookId, cancellationToken);
    }

    public Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        cancellationToken.ThrowIfCancellationRequested();
        return _readingProgressStore.SaveAsync(progress, cancellationToken);
    }
}
