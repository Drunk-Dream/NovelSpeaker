namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Stable source identity for one speech segment. Playback order is intentionally not part of it.
/// </summary>
public readonly record struct StableSpeechSegmentIdentity(
    SpeechSegmentKind Kind,
    int SourceStartOffset,
    int SourceLength)
{
    public static StableSpeechSegmentIdentity Body(int sourceStartOffset, int sourceLength)
    {
        if (sourceStartOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStartOffset));
        }

        if (sourceLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLength));
        }

        return new StableSpeechSegmentIdentity(
            SpeechSegmentKind.Body,
            sourceStartOffset,
            sourceLength);
    }

    public static StableSpeechSegmentIdentity ChapterTitle() =>
        new(SpeechSegmentKind.ChapterTitle, 0, 0);
}
