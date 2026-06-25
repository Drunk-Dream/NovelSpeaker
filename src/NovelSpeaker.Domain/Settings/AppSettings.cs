using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Domain.Settings;

/// <summary>
/// Stores non-sensitive desktop settings for the current user.
/// </summary>
public sealed record AppSettings(
    bool EnableLongParagraphSplitting,
    int LongParagraphThreshold,
    int DefaultSpeakSpeed = 10,
    int PrefetchCount = 2,
    string LogLevel = "Information",
    string Theme = "System",
    long? SelectedTtsRuleId = null)
{
    public const int DefaultSpeakSpeedValue = 10;
    public const int DefaultPrefetchCountValue = 2;
    public const string DefaultLogLevel = "Information";
    public const string DefaultTheme = "System";

    public static IReadOnlyList<string> SupportedLogLevels { get; } =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    public static IReadOnlyList<string> SupportedThemes { get; } =
        ["System", "Light", "Dark"];

    public static AppSettings Default { get; } =
        new(
            TextSegmentationOptions.Default.EnableLongParagraphSplitting,
            TextSegmentationOptions.Default.LongParagraphThreshold,
            DefaultSpeakSpeedValue,
            DefaultPrefetchCountValue,
            DefaultLogLevel,
            DefaultTheme,
            null);

    public TextSegmentationOptions ToTextSegmentationOptions()
    {
        return new TextSegmentationOptions(
            EnableLongParagraphSplitting,
            LongParagraphThreshold).Normalize();
    }

    public AppSettings Normalize()
    {
        var segmentation = ToTextSegmentationOptions();
        return this with
        {
            EnableLongParagraphSplitting = segmentation.EnableLongParagraphSplitting,
            LongParagraphThreshold = segmentation.LongParagraphThreshold,
            DefaultSpeakSpeed = DefaultSpeakSpeed <= 0 ? DefaultSpeakSpeedValue : DefaultSpeakSpeed,
            PrefetchCount = PrefetchCount < 0 ? DefaultPrefetchCountValue : PrefetchCount,
            LogLevel = NormalizeOption(LogLevel, SupportedLogLevels, DefaultLogLevel),
            Theme = NormalizeOption(Theme, SupportedThemes, DefaultTheme)
        };
    }

    private static string NormalizeOption(string? value, IReadOnlyList<string> supportedValues, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        foreach (var candidate in supportedValues)
        {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return defaultValue;
    }
}
