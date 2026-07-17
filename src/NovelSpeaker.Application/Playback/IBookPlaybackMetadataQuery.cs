namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Queries persisted playback metadata without reading or assembling chapter text.
/// </summary>
public interface IBookPlaybackMetadataQuery
{
    Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken);

    Task<PlaybackChapterMetadata?> GetChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken);
}
