using System.IO;

namespace NovelSpeaker.StyleGallery;

public sealed record GalleryCommandLineOptions(
    bool ScreenshotMode,
    GalleryThemeChoice Theme,
    string Task,
    string OutputDirectory,
    string? SceneName)
{
    public static GalleryCommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var screenshotMode = false;
        var theme = GalleryThemeChoice.Light;
        var task = "03";
        string? outputDirectory = null;
        string? sceneName = null;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--screenshot":
                    screenshotMode = true;
                    break;
                case "--theme" when index + 1 < args.Count:
                    theme = GalleryThemeExtensions.Parse(args[++index]);
                    break;
                case "--task" when index + 1 < args.Count:
                    task = ParseTask(args[++index]);
                    break;
                case "--output" when index + 1 < args.Count:
                    outputDirectory = args[++index];
                    break;
                case "--scene" when index + 1 < args.Count:
                    sceneName = args[++index];
                    break;
                case "--help":
                case "-h":
                    throw new GalleryUsageException(UsageText);
                default:
                    throw new GalleryUsageException($"Unknown Style Gallery argument '{args[index]}'.{Environment.NewLine}{UsageText}");
            }
        }

        if (!screenshotMode && sceneName is not null)
        {
            throw new GalleryUsageException("--scene requires --screenshot.");
        }

        outputDirectory ??= Path.Combine("artifacts", "visual-review", task);
        return new GalleryCommandLineOptions(screenshotMode, theme, task, outputDirectory, sceneName);
    }

    public static string UsageText =>
        "Usage: dotnet run --project tools/NovelSpeaker.StyleGallery -- --screenshot --task 13 --scene navigation-feedback --theme all --output artifacts/visual-review/13";

    private static string ParseTask(string value) =>
        value is "03" or "04" or "05" or "06" or "07" or "08" or "11" or "12" or "13"
            ? value
            : throw new GalleryUsageException($"Task must be '03', '04', '05', '06', '07', '08', '11', '12' or '13', but was '{value}'.{Environment.NewLine}{UsageText}");
}

public enum GalleryThemeChoice
{
    Light,
    Dark,
    All
}

public sealed class GalleryUsageException : Exception
{
    public GalleryUsageException(string message)
        : base(message)
    {
    }
}
