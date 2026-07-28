using System.Text;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Logical audio identity kept separate from runtime playback order and text profile identity.
/// </summary>
public sealed record AudioCacheIdentity(
    string ChapterId,
    StableSpeechSegmentIdentity Segment,
    Fingerprint SpeechTextHash,
    SynthesisProfileFingerprint SynthesisProfile)
{
    public static AudioCacheIdentity Create(
        string chapterId,
        StableSpeechSegmentIdentity segment,
        string speechText,
        SynthesisProfileFingerprint synthesisProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);
        ArgumentNullException.ThrowIfNull(speechText);
        ArgumentNullException.ThrowIfNull(synthesisProfile);

        return new AudioCacheIdentity(
            chapterId,
            segment,
            Fingerprint.Sha256(Encoding.UTF8.GetBytes(speechText)),
            synthesisProfile);
    }
}
