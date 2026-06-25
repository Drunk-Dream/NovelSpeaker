namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Loads persisted books for playback, separating chapter metadata from on-demand text segmentation.
/// </summary>
public interface IBookPlaybackContentService
{
    Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken);

    Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken);
}
