using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Playback.Cache;

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
    /// <summary>Creates the single cache identity shared by current playback and prefetch.</summary>
    public AudioCacheKey ToCacheKey()
    {
        return AudioCacheKey.FromPlayback(
            BookId,
            ChapterIndex,
            SegmentIndex,
            RuleId,
            SpeakSpeed,
            SpeechText);
    }
}
