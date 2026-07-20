namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Coordinates playback cleanup and metadata refresh when a book changes outside playback.
/// </summary>
public interface IPlaybackBookCommands : IPlaybackSnapshotSource
{
    Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken);
    Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken);
}
