namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Owns playback checkpoint mapping and persistence for the current session.
/// </summary>
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

    public long GetCurrentPositionMillisecondsForSave(
        PlaybackSessionState session,
        LocalAudioPlaybackSnapshot currentAudio)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(currentAudio);

        if (session.HasLoadedAudio &&
            string.Equals(currentAudio.BookId, session.BookId, StringComparison.Ordinal) &&
            currentAudio.ChapterIndex == session.ChapterIndex &&
            currentAudio.SegmentIndex == session.SegmentIndex)
        {
            return currentAudio.PositionMilliseconds;
        }

        return session.PositionForSave;
    }

    public Task SaveAsync(
        PlaybackSessionState session,
        long positionMilliseconds,
        LocalAudioPlaybackSnapshot currentAudio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(currentAudio);
        cancellationToken.ThrowIfCancellationRequested();

        if (session.HasLoadedAudio)
        {
            session.UpdateAudio(currentAudio);
        }

        session.SetPositionForSave(positionMilliseconds);
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
