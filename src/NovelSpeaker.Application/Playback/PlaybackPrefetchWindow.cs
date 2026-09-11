using NovelSpeaker.Application.Cache.Audio;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Explicit ordered prefetch window. Earlier requests have higher prefetch priority.
/// </summary>
public sealed record PlaybackPrefetchWindow(
    Guid SessionId,
    IReadOnlyList<AudioGenerationRequest> Requests)
{
    public PlaybackPrefetchWindow(Guid sessionId, IEnumerable<AudioGenerationRequest> requests)
        : this(sessionId, requests.ToArray())
    {
    }
}
