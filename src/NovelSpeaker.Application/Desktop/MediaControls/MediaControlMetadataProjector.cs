using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Application.Desktop.MediaControls;

internal static class MediaControlMetadataProjector
{
    public static MediaControlMetadata Project(PlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new MediaControlMetadata(
            snapshot.ChapterTitle ?? string.Empty,
            snapshot.BookTitle ?? string.Empty,
            snapshot.State switch
            {
                PlaybackState.Playing => MediaControlPlaybackStatus.Playing,
                PlaybackState.Paused => MediaControlPlaybackStatus.Paused,
                _ => MediaControlPlaybackStatus.Stopped
            });
    }
}
