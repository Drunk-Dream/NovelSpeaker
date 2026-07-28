using NovelSpeaker.Application.Cache;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Metadata-only input for one chapter in a current-configuration cache coverage query.
/// </summary>
public sealed record CurrentCacheChapterQuery(
    string ChapterId,
    int ChapterIndex,
    bool ReadChapterTitle,
    Fingerprint? ChapterTitleSpeechTextHash)
{
    public bool HasChapterTitle => ReadChapterTitle && ChapterTitleSpeechTextHash is not null;
}
