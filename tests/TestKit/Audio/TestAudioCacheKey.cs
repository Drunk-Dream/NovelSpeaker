using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.TestKit.Audio;

internal static class TestAudioCacheKey
{
    public static AudioCacheKey Create(
        string bookId,
        int chapterIndex,
        int sourceStartOffset,
        long ruleId,
        int speakSpeed,
        string speechText) =>
        AudioCacheKey.FromIdentity(AudioCacheIdentity.Create(
            $"{bookId}/chapter/{chapterIndex}",
            StableSpeechSegmentIdentity.Body(sourceStartOffset, 1),
            speechText,
            CreateProfile(ruleId, speakSpeed)));

    public static AudioCacheKey CreateTitle(
        string bookId,
        int chapterIndex,
        long ruleId,
        int speakSpeed,
        string speechText) =>
        AudioCacheKey.FromIdentity(AudioCacheIdentity.Create(
            $"{bookId}/chapter/{chapterIndex}",
            StableSpeechSegmentIdentity.ChapterTitle(),
            speechText,
            CreateProfile(ruleId, speakSpeed)));

    private static SynthesisProfileFingerprint CreateProfile(long ruleId, int speakSpeed)
    {
        var rule = new HttpSpeechProviderConfiguration($"https://cache-key.invalid/{ruleId}", "GET", new Dictionary<string, string>(), null, null);
        return SynthesisProfileFingerprint.Create(ProviderSynthesisFingerprint.Create(rule), speakSpeed);
    }
}
