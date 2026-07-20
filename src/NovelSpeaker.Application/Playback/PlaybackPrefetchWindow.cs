namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Explicit ordered prefetch window. Earlier requests have higher prefetch priority.
/// </summary>
public sealed record PlaybackPrefetchWindow(
    Guid SessionId,
    IReadOnlyList<PlaybackAudioRequest> Requests)
{
    public PlaybackPrefetchWindow(Guid sessionId, IEnumerable<PlaybackAudioRequest> requests)
        : this(sessionId, requests.ToArray())
    {
    }
}
