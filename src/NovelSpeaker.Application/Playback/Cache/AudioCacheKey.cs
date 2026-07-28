using NovelSpeaker.Application.Cache;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Represents the versioned, stable cache identity for one generated playback audio item.
/// </summary>
public sealed record AudioCacheKey
{
    public const string CurrentVersion = "v2";

    private AudioCacheKey(
        string value,
        string fileNameBase,
        AudioCacheIdentity identity)
    {
        Value = value;
        FileNameBase = fileNameBase;
        Identity = identity;
    }

    public string Value { get; }

    public string FileNameBase { get; }

    public AudioCacheIdentity Identity { get; }

    public string Version => CurrentVersion;

    public string Shard => FileNameBase[..Math.Min(2, FileNameBase.Length)];

    public static AudioCacheKey FromIdentity(AudioCacheIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var writer = new CanonicalIdentityWriter();
        writer.Add("schema", CurrentVersion);
        writer.Add("chapter", identity.ChapterId);
        writer.Add("segment-kind", ((int)identity.Segment.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (identity.Segment.Kind == SpeechSegmentKind.Body)
        {
            writer.Add("source-start", identity.Segment.SourceStartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.Add("source-length", identity.Segment.SourceLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        writer.Add("speech-text-hash", identity.SpeechTextHash.Hex);
        writer.Add("synthesis-profile", identity.SynthesisProfile.Hex);
        var hash = writer.Build().Hex;
        return new AudioCacheKey($"{CurrentVersion}:{hash}", hash, identity);
    }

}
