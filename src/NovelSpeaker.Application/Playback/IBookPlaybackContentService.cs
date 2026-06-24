namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Loads persisted books and projects their chapters into playback-ready speech segments.
/// </summary>
public interface IBookPlaybackContentService
{
    Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken);
}
