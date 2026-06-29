using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Converts one chapter text into ordered runtime speech segments.
/// </summary>
public interface ITextSegmenter
{
    IReadOnlyList<SpeechSegment> Segment(
        string chapterText,
        TextSegmentationOptions options);
}
