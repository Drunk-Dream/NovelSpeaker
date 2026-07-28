namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Identifies the kind of a speech segment independently from its playback order.
/// </summary>
public enum SpeechSegmentKind
{
    Body = 0,
    ChapterTitle = 1
}
