namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Global runtime options that control how chapter text is split into speech segments.
/// </summary>
public sealed record TextSegmentationOptions(
    bool EnableLongParagraphSplitting,
    int LongParagraphThreshold)
{
    public const int DefaultLongParagraphThreshold = 300;
    public const int MinimumLongParagraphThreshold = 50;

    public static TextSegmentationOptions Default { get; } =
        new(true, DefaultLongParagraphThreshold);

    public TextSegmentationOptions Normalize()
    {
        var threshold = LongParagraphThreshold < MinimumLongParagraphThreshold
            ? MinimumLongParagraphThreshold
            : LongParagraphThreshold;

        return this with { LongParagraphThreshold = threshold };
    }
}
