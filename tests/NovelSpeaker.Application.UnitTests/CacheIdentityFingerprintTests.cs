using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class CacheIdentityFingerprintTests
{
    [Fact]
    public void Text_profile_uses_the_current_narratable_content_contract()
    {
        var profile = TextProfileFingerprint.Create(TextSegmentationOptions.Default, []);

        Assert.Equal(2, profile.SchemaVersion);
    }

    [Fact]
    public void Body_identity_does_not_use_runtime_segment_index_and_title_has_its_own_kind()
    {
        var body = new SpeechSegment(42, 17, 8, "正文", "正文");
        var title = new SpeechSegment(0, 0, 0, "第一章", "第一章", IsChapterTitle: true);

        Assert.Equal(
            new StableSpeechSegmentIdentity(SpeechSegmentKind.Body, 17, 8),
            body.StableIdentity);
        Assert.Equal(SpeechSegmentKind.ChapterTitle, title.SegmentKind);
        Assert.Equal(StableSpeechSegmentIdentity.ChapterTitle(), title.StableIdentity);
        Assert.Equal(body.StableIdentity, new SpeechSegment(0, 17, 8, "正文", "正文").StableIdentity);
    }

    [Fact]
    public void Text_profile_excludes_display_only_rules_and_rule_metadata()
    {
        var id = Guid.NewGuid();
        var first = TextProfileFingerprint.Create(
            new TextSegmentationOptions(true, 300),
            [
                new RegexReplacementRule(
                    id,
                    "原名称",
                    true,
                    0,
                    "正文",
                    "语音",
                    RegexReplacementScope.Speech,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                new RegexReplacementRule(
                    Guid.NewGuid(),
                    "仅显示",
                    true,
                    1,
                    "正文",
                    "显示",
                    RegexReplacementScope.Display,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)
            ]);
        var second = TextProfileFingerprint.Create(
            new TextSegmentationOptions(true, 300),
            [new RegexReplacementRule(
                id,
                "改名",
                true,
                0,
                "正文",
                "语音",
                RegexReplacementScope.Speech,
                DateTimeOffset.UtcNow.AddDays(1),
                DateTimeOffset.UtcNow.AddDays(1))]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Tts_rule_fingerprint_excludes_name_enabled_and_concurrency_but_tracks_request_semantics()
    {
        var normalizer = new TtsRuleNormalizer();
        var first = TtsRuleFingerprint.Create(normalizer.Normalize(CreateRule(
            name: "原名称",
            isEnabled: true,
            concurrentRate: "2/s",
            body: "{\"text\":\"{{speakText}}\"}")));
        var metadataOnly = TtsRuleFingerprint.Create(normalizer.Normalize(CreateRule(
            name: "改名",
            isEnabled: false,
            concurrentRate: "20/s",
            body: "{\"text\":\"{{speakText}}\"}")));
        var changedBody = TtsRuleFingerprint.Create(normalizer.Normalize(CreateRule(
            name: "改名",
            isEnabled: false,
            concurrentRate: "20/s",
            body: "{\"value\":\"{{speakText}}\"}")));

        Assert.Equal(first, metadataOnly);
        Assert.NotEqual(first, changedBody);
    }

    [Fact]
    public void Audio_identity_changes_with_speech_text_but_not_text_profile()
    {
        var rule = TtsRuleFingerprint.Create(new TtsRuleNormalizer().Normalize(CreateRule(
            "规则", true, null, "{{speakText}}")));
        var synthesis = SynthesisProfileFingerprint.Create(rule, 10);
        var identity = AudioCacheIdentity.Create(
            "chapter-1",
            StableSpeechSegmentIdentity.Body(10, 5),
            "同一语音文本",
            synthesis);
        var same = AudioCacheIdentity.Create(
            "chapter-1",
            StableSpeechSegmentIdentity.Body(10, 5),
            "同一语音文本",
            synthesis);
        var changed = AudioCacheIdentity.Create(
            "chapter-1",
            StableSpeechSegmentIdentity.Body(10, 5),
            "另一语音文本",
            synthesis);

        Assert.Equal(identity, same);
        Assert.NotEqual(identity, changed);
    }

    private static NovelSpeaker.Domain.Speech.HttpTtsRule CreateRule(
        string name,
        bool isEnabled,
        string? concurrentRate,
        string body) =>
        new(
            7,
            name,
            " https://example.test/tts ",
            " Application/JSON ",
            concurrentRate,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [" X-Test "] = " value "
            },
            " post ",
            body,
            true,
            null,
            isEnabled,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
}
