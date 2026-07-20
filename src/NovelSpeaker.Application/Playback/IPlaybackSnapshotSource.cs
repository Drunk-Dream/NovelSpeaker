namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Provides the immutable playback state projection consumed by shell and playback views.
/// </summary>
public interface IPlaybackSnapshotSource
{
    PlaybackSnapshot CurrentSnapshot { get; }

    event EventHandler<PlaybackSnapshot>? SnapshotChanged;
}
