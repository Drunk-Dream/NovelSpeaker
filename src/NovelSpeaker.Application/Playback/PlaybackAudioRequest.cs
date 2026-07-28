using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes one runtime TTS generation request for a specific book segment.
/// </summary>
public sealed record PlaybackAudioRequest(
    string BookId,
    int ChapterIndex,
    int SegmentIndex,
    string SpeechText,
    long RuleId,
    HttpTtsRule SourceRule,
    NormalizedHttpTtsRule NormalizedRule,
    int SpeakSpeed,
    Guid SessionId)
{
    /// <summary>Stable persisted chapter identity supplied by the content loader.</summary>
    public string? ChapterId { get; init; }

    /// <summary>Source identity independent from the runtime playback order.</summary>
    public StableSpeechSegmentIdentity? StableSegmentIdentity { get; init; }

    /// <summary>Creates the single v2 cache identity shared by playback and prefetch.</summary>
    public AudioCacheIdentity ToCacheIdentity()
    {
        var chapterId = ChapterId ?? $"{BookId}/chapter/{ChapterIndex}";
        var segmentIdentity = StableSegmentIdentity ??
            throw new InvalidOperationException("播放音频请求缺少稳定段身份。");
        var synthesisProfile = SynthesisProfileFingerprint.Create(
            TtsRuleFingerprint.Create(NormalizedRule),
            SpeakSpeed);
        return AudioCacheIdentity.Create(
            chapterId,
            segmentIdentity,
            SpeechText,
            synthesisProfile);
    }

    public AudioCacheKey ToCacheKey()
    {
        return AudioCacheKey.FromIdentity(ToCacheIdentity());
    }
}
