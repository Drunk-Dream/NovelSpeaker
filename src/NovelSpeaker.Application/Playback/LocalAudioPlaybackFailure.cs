namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Captures the audio facts and session identity that produced a low-level failure.
/// </summary>
public sealed record LocalAudioPlaybackFailure(
    LocalAudioPlaybackSnapshot Snapshot,
    PlaybackErrorEventArgs Error);
