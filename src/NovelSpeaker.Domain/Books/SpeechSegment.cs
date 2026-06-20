namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents one runtime speech unit mapped to a range in <see cref="Chapter.Content"/>.
/// </summary>
public sealed record SpeechSegment(
    int SegmentIndex,
    int StartOffset,
    int Length,
    string DisplayText,
    string SpeechText);
