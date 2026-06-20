using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Converts one persisted chapter into ordered runtime speech segments.
/// </summary>
public interface ITextSegmenter
{
    IReadOnlyList<SpeechSegment> Segment(
        Chapter chapter,
        TextSegmentationOptions options);
}
