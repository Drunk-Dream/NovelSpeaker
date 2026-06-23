namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Carries a safe, user-visible playback failure description.
/// </summary>
public sealed class PlaybackErrorEventArgs : EventArgs
{
    public PlaybackErrorEventArgs(PlaybackErrorKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }

    public PlaybackErrorKind Kind { get; }
    public string Message { get; }
}
