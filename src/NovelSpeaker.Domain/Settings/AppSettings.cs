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
    string? BookFileNameTemplate = "{{name}} 作者：{{author}}",
    long? SelectedTtsRuleId = null)
{
    public const int MinSpeakSpeed = 1;
    public const int MaxSpeakSpeed = 20;
    public const int DefaultSpeakSpeedValue = 10;
    public const int DefaultPrefetchCountValue = 2;
    public const string DefaultLogLevel = "Information";
    public const string DefaultTheme = "System";
    public const string DefaultBookFileNameTemplate = "{{name}} 作者：{{author}}";

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
            DefaultBookFileNameTemplate,
            null);

    public TextSegmentationOptions ToTextSegmentationOptions()
    {
        return new TextSegmentationOptions(
            EnableLongParagraphSplitting,
            LongParagraphThreshold).Normalize();
    }

    public static bool IsValidSpeakSpeed(int speakSpeed)
    {
        return speakSpeed >= MinSpeakSpeed && speakSpeed <= MaxSpeakSpeed;
    }

    public static int NormalizeSpeakSpeed(int speakSpeed)
    {
        if (speakSpeed <= 0)
        {
            return DefaultSpeakSpeedValue;
        }

        return Math.Clamp(speakSpeed, MinSpeakSpeed, MaxSpeakSpeed);
    }

    public AppSettings Normalize()
    {
        var segmentation = ToTextSegmentationOptions();
        return this with
        {
            EnableLongParagraphSplitting = segmentation.EnableLongParagraphSplitting,
            LongParagraphThreshold = segmentation.LongParagraphThreshold,
            DefaultSpeakSpeed = NormalizeSpeakSpeed(DefaultSpeakSpeed),
            PrefetchCount = PrefetchCount < 0
                ? DefaultPrefetchCountValue
                : Math.Min(PrefetchCount, DefaultPrefetchCountValue),
            LogLevel = NormalizeOption(LogLevel, SupportedLogLevels, DefaultLogLevel),
            Theme = NormalizeOption(Theme, SupportedThemes, DefaultTheme),
            BookFileNameTemplate = NormalizeFileNameTemplate(BookFileNameTemplate)
        };
    }

    private static string NormalizeFileNameTemplate(string? value)
    {
        if (value is null)
        {
            return DefaultBookFileNameTemplate;
        }

        return value.Trim();
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
