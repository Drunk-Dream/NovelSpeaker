using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Audio;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Explicit ordered prefetch window. Earlier requests have higher prefetch priority.
/// </summary>
public sealed record PlaybackPrefetchWindow(
    Guid SessionId,
    IReadOnlyList<AudioGenerationRequest> Requests)
{
    /// <summary>Monotonically orders window updates within the owning prefetch coordinator.</summary>
    public long Revision { get; init; }

    /// <summary>Preserves matching active work while replacing only future requests.</summary>
    public AudioCacheKey? KeepActiveKey { get; init; }

    public PlaybackPrefetchWindow(Guid sessionId, IEnumerable<AudioGenerationRequest> requests)
        : this(sessionId, requests.ToArray())
    {
    }
}
