using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

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
        var rule = new NormalizedHttpTtsRule(
            ruleId,
            "test",
            NormalizedTemplate.Parse($"https://cache-key.invalid/{ruleId}"),
            new Dictionary<string, NormalizedTemplate>(),
            "GET",
            null,
            false,
            "audio/mpeg",
            null);
        return SynthesisProfileFingerprint.Create(TtsRuleFingerprint.Create(rule), speakSpeed);
    }
}
