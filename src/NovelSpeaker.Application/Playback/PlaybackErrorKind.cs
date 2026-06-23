namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Classifies local playback failures into user-visible categories.
/// </summary>
public enum PlaybackErrorKind
{
    FileNotFound,
    UnsupportedFormat,
    AudioDecode,
    OutputDevice,
    Cancelled,
    Unknown
}
