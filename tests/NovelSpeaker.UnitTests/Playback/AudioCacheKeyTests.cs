using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class AudioCacheKeyTests
{
    [Fact]
    public void FromPlayback_returns_versioned_value_and_sha256_file_name_base()
    {
        var key = AudioCacheKey.FromPlayback("book-1", 2, 3, 42, 10, "测试文本");

        Assert.StartsWith("v1:", key.Value);
        Assert.Equal(64, key.FileNameBase.Length);
        Assert.Equal(key.FileNameBase, key.Value["v1:".Length..]);
        Assert.Equal(key.FileNameBase[..2], key.Shard);
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
