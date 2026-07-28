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
    long CacheLimitBytes = 2L * 1024 * 1024 * 1024,
    long? SelectedTtsRuleId = null,
    MainWindowCloseBehavior MainWindowCloseBehavior = MainWindowCloseBehavior.MinimizeToTray,
    bool StartMinimizedToTray = false,
    double? MiniPlayerLeft = null,
    double? MiniPlayerTop = null,
    bool MiniPlayerTopmost = false,
    bool ReadChapterTitle = false)
{
    public const int MinSpeakSpeed = 1;
    public const int MaxSpeakSpeed = 20;
    public const int DefaultSpeakSpeedValue = 10;
    public const int DefaultPrefetchCountValue = 2;
    public const string DefaultLogLevel = "Information";
    public const string DefaultTheme = "System";
    public const string DefaultBookFileNameTemplate = "{{name}} 作者：{{author}}";
    public const long DefaultCacheLimitBytes = 2L * 1024 * 1024 * 1024;
    public const long MinCacheLimitBytes = 256L * 1024 * 1024;

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
            DefaultCacheLimitBytes,
            null,
            MainWindowCloseBehavior.MinimizeToTray,
            false,
            null,
            null,
            false,
            false);

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
            BookFileNameTemplate = NormalizeFileNameTemplate(BookFileNameTemplate),
            CacheLimitBytes = NormalizeCacheLimitBytes(CacheLimitBytes),
            MainWindowCloseBehavior = Enum.IsDefined(MainWindowCloseBehavior)
                ? MainWindowCloseBehavior
                : MainWindowCloseBehavior.MinimizeToTray,
            MiniPlayerLeft = NormalizeCoordinate(MiniPlayerLeft),
            MiniPlayerTop = NormalizeCoordinate(MiniPlayerTop)
        };
    }

    public static long NormalizeCacheLimitBytes(long cacheLimitBytes)
    {
        if (cacheLimitBytes <= 0)
        {
            return DefaultCacheLimitBytes;
        }

        return Math.Max(MinCacheLimitBytes, cacheLimitBytes);
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

    private static double? NormalizeCoordinate(double? value) =>
        value is { } coordinate && double.IsFinite(coordinate)
            ? coordinate
            : null;
}
