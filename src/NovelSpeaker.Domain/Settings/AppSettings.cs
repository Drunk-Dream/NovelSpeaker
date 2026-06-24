using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Domain.Settings;

/// <summary>
/// Stores non-sensitive desktop settings for the current user.
/// </summary>
public sealed record AppSettings(
    bool EnableLongParagraphSplitting,
    int LongParagraphThreshold,
    long? SelectedTtsRuleId = null)
{
    public static AppSettings Default { get; } =
        new(
            TextSegmentationOptions.Default.EnableLongParagraphSplitting,
            TextSegmentationOptions.Default.LongParagraphThreshold,
            null);

    public TextSegmentationOptions ToTextSegmentationOptions()
    {
        return new TextSegmentationOptions(
            EnableLongParagraphSplitting,
            LongParagraphThreshold).Normalize();
    }
}
