using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Adds playback-only chapter title segments without changing source-text offsets.
/// </summary>
internal static class PlaybackSpeechSegmentComposer
{
    public static IReadOnlyList<SpeechSegment> Compose(
        string chapterTitle,
        IReadOnlyList<SpeechSegment> contentSegments,
        bool readChapterTitle)
    {
        ArgumentNullException.ThrowIfNull(contentSegments);

        if (!readChapterTitle || string.IsNullOrWhiteSpace(chapterTitle))
        {
            return contentSegments;
        }

        var segments = new List<SpeechSegment>(contentSegments.Count + 1)
        {
            new(0, 0, 0, chapterTitle, chapterTitle, IsChapterTitle: true)
        };

        for (var index = 0; index < contentSegments.Count; index++)
        {
            segments.Add(contentSegments[index] with { SegmentIndex = index + 1 });
        }

        return segments;
    }
}
