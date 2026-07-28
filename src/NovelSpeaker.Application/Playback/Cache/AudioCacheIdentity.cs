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

        return CreateFromSpeechTextHash(
            chapterId,
            segment,
            Fingerprint.Sha256(Encoding.UTF8.GetBytes(speechText)),
            synthesisProfile);
    }

    /// <summary>
    /// Rehydrates the same identity when a persisted speech plan already contains
    /// the final speech-text hash and the source text is intentionally unavailable.
    /// </summary>
    public static AudioCacheIdentity CreateFromSpeechTextHash(
        string chapterId,
        StableSpeechSegmentIdentity segment,
        Fingerprint speechTextHash,
        SynthesisProfileFingerprint synthesisProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);
        ArgumentNullException.ThrowIfNull(speechTextHash);
        ArgumentNullException.ThrowIfNull(synthesisProfile);

        return new AudioCacheIdentity(
            chapterId,
            segment,
            speechTextHash,
            synthesisProfile);
    }
}
