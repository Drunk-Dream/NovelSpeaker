using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class AudioCacheKeyTests
{
    [Fact]
    public void FromPlayback_returns_versioned_value_and_sha256_file_name_base()
    {
        var key = AudioCacheKey.FromPlayback("book-1", 2, 3, 42, 10, "测试文本");

        Assert.StartsWith("v1:", key.Value);
        Assert.Equal(
            "v1:9f49a2ad89223cc7dab769a350ba612c0be5383e90fced75e2077a05bb5cf60d",
            key.Value);
        Assert.Equal(64, key.FileNameBase.Length);
        Assert.Equal(key.FileNameBase, key.Value["v1:".Length..]);
        Assert.Equal(key.FileNameBase[..2], key.Shard);
    }

    [Fact]
    public void Playback_request_uses_the_byte_compatible_cache_key_conversion()
    {
        var request = new PlaybackAudioRequest(
            "book-1",
            2,
            3,
            "测试文本",
            42,
            null!,
            null!,
            10,
            Guid.NewGuid());

        var key = request.ToCacheKey();

        Assert.Equal(
            "v1:9f49a2ad89223cc7dab769a350ba612c0be5383e90fced75e2077a05bb5cf60d",
            key.Value);
    }

    [Fact]
    public void FromPlayback_changes_when_identity_inputs_change()
    {
        var baseline = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        var differentRule = AudioCacheKey.FromPlayback("book-1", 0, 0, 2, 10, "第一段");
        var differentSpeed = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 11, "第一段");
        var differentText = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第二段");

        Assert.NotEqual(baseline, differentRule);
        Assert.NotEqual(baseline, differentSpeed);
        Assert.NotEqual(baseline, differentText);
    }
}
