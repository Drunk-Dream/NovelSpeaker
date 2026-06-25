using System.Security.Cryptography;
using System.Text;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the future-stable cache identity for one generated playback audio item.
/// </summary>
public sealed record AudioCacheKey(string Value, string FileNameBase)
{
    public const string CurrentVersion = "v1";

    public string Version => CurrentVersion;

    public string Shard => FileNameBase[..Math.Min(2, FileNameBase.Length)];

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
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        return new AudioCacheKey($"{CurrentVersion}:{hash}", hash);
    }
}
