namespace NovelSpeaker.Application.Playback;

internal enum PlaybackProgressSaveReason
{
    SegmentCompleted,
    Pause,
    Stop,
    SessionReplaced,
    ApplicationExit
}

/// <summary>
/// Owns progress persistence semantics, including character-offset mapping and save timing.
/// </summary>
internal sealed class PlaybackProgressService
{
    private readonly IReadingProgressStore _readingProgressStore;

    public PlaybackProgressService(IReadingProgressStore readingProgressStore)
    {
        _readingProgressStore = readingProgressStore;
    }

    public Task<ReadingProgressEntry?> RestoreAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return _readingProgressStore.GetAsync(bookId, cancellationToken);
    }

    public Task SaveAsync(
        PlaybackSessionState session,
        PlaybackProgressSaveReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        _ = reason;
        var chapter = session.Book.Chapters.FirstOrDefault(
            candidate => candidate.ChapterIndex == session.ChapterIndex);
        var characterOffset = chapter is not null &&
                              session.SegmentIndex >= 0 &&
                              session.SegmentIndex < chapter.Segments.Count
            ? chapter.Segments[session.SegmentIndex].StartOffset
            : 0;

        return _readingProgressStore.SaveAsync(
            new PlaybackProgressUpdate(
                session.Book.BookId,
                session.ChapterIndex,
                session.SegmentIndex,
                characterOffset,
                session.PositionForSave),
            cancellationToken);
    }
}
