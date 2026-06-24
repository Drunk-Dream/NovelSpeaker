using System.Security.Cryptography;
using System.Text;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the future-stable cache identity for one generated playback audio item.
/// </summary>
public sealed record AudioCacheKey(string Value)
{
    public static AudioCacheKey FromPlayback(
        string bookId,
        int chapterIndex,
        int segmentIndex,
        long ruleId,
        int speakSpeed,
        string speechText)
    {
        var raw = $"{bookId}|{chapterIndex}|{segmentIndex}|{ruleId}|{speakSpeed}|{speechText}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return new AudioCacheKey(Convert.ToHexString(bytes).ToLowerInvariant());
    }
}
