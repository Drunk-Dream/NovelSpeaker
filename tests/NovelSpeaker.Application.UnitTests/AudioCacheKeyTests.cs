using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class AudioCacheKeyTests
{
    [Fact]
    public void FromIdentity_uses_v2_and_keeps_runtime_order_out_of_the_key()
    {
        var profile = CreateProfile();
        var identity = AudioCacheIdentity.Create(
            "chapter-1",
            StableSpeechSegmentIdentity.Body(42, 17),
            "测试文本",
            profile);

        var key = AudioCacheKey.FromIdentity(identity);
        var sameIdentityAfter_runtime_reordering = AudioCacheIdentity.Create(
            "chapter-1",
            StableSpeechSegmentIdentity.Body(42, 17),
            "测试文本",
            profile);

        Assert.StartsWith("v2:", key.Value);
        Assert.Equal(AudioCacheKey.CurrentVersion, key.Version);
        Assert.Equal(key, AudioCacheKey.FromIdentity(sameIdentityAfter_runtime_reordering));
        Assert.Equal(64, key.FileNameBase.Length);
        Assert.Equal(key.FileNameBase, key.Value["v2:".Length..]);
    }

    [Fact]
    public void FromIdentity_separates_body_and_title_and_tracks_text_and_synthesis_changes()
    {
        var profile = CreateProfile();
        var body = AudioCacheKey.FromIdentity(AudioCacheIdentity.Create(
            "chapter-1",
            StableSpeechSegmentIdentity.Body(0, 3),
            "正文",
            profile));
        var title = AudioCacheKey.FromIdentity(AudioCacheIdentity.Create(
            "chapter-1",
            StableSpeechSegmentIdentity.ChapterTitle(),
            "第一章",
            profile));
        var changedText = AudioCacheKey.FromIdentity(AudioCacheIdentity.Create(
            "chapter-1",
            StableSpeechSegmentIdentity.Body(0, 3),
            "正文已改",
            profile));
        var changedSynthesis = AudioCacheKey.FromIdentity(AudioCacheIdentity.Create(
            "chapter-1",
            StableSpeechSegmentIdentity.Body(0, 3),
            "正文",
            CreateProfile(speakSpeed: 11)));

        Assert.NotEqual(body, title);
        Assert.NotEqual(body, changedText);
        Assert.NotEqual(body, changedSynthesis);
    }

    [Fact]
    public void AudioCacheIdentity_does_not_change_when_only_title_playback_setting_changes()
    {
        var profile = CreateProfile();
        var body = StableSpeechSegmentIdentity.Body(17, 8);

        var withoutTitle = AudioCacheKey.FromIdentity(AudioCacheIdentity.Create(
            "chapter-1", body, "正文", profile));
        var withTitle = AudioCacheKey.FromIdentity(AudioCacheIdentity.Create(
            "chapter-1", body, "正文", profile));

        Assert.Equal(withoutTitle, withTitle);
    }

    [Fact]
    public void FromSpeechTextHash_matches_the_key_built_from_the_original_speech_text()
    {
        var profile = CreateProfile();
        var segment = StableSpeechSegmentIdentity.Body(17, 8);
        var fromText = AudioCacheKey.FromIdentity(
            AudioCacheIdentity.Create("chapter-1", segment, "持久化文本", profile));

        var fromPersistedHash = AudioCacheKey.FromSpeechTextHash(
            "chapter-1",
            segment,
            Fingerprint.Sha256("持久化文本"),
            profile);

        Assert.Equal(fromText, fromPersistedHash);
    }

    private static SynthesisProfileFingerprint CreateProfile(int speakSpeed = 10)
    {
        var normalizedRule = new NormalizedHttpTtsRule(
            42,
            "测试规则",
            NormalizedTemplate.Parse("https://example.test/tts?text={{speakText}}"),
            new Dictionary<string, NormalizedTemplate>(),
            "GET",
            null,
            false,
            "audio/mpeg",
            "2/s");
        return SynthesisProfileFingerprint.Create(
            TtsRuleFingerprint.Create(normalizedRule),
            speakSpeed);
    }
}
