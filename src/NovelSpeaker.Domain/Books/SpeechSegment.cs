namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents one runtime speech unit mapped to a range in one chapter text.
/// </summary>
public sealed record SpeechSegment(
    int SegmentIndex,
    int StartOffset,
    int Length,
    string DisplayText,
    string SpeechText,
    bool IsChapterTitle = false)
{
    /// <summary>
    /// Returns the stable kind used by cache and plan identities.
    /// </summary>
    public SpeechSegmentKind SegmentKind =>
        IsChapterTitle ? SpeechSegmentKind.ChapterTitle : SpeechSegmentKind.Body;

    /// <summary>
    /// Returns the source identity without the runtime playback index.
    /// </summary>
    public StableSpeechSegmentIdentity StableIdentity =>
        IsChapterTitle
            ? StableSpeechSegmentIdentity.ChapterTitle()
            : StableSpeechSegmentIdentity.Body(StartOffset, Length);
}
