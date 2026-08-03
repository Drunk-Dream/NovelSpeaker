using System.IO;

namespace NovelSpeaker.StyleGallery;

public sealed record GalleryCommandLineOptions(
    bool ScreenshotMode,
    GalleryThemeChoice Theme,
    string OutputDirectory,
    string? SceneName)
{
    public static GalleryCommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var screenshotMode = false;
        var theme = GalleryThemeChoice.Light;
        var outputDirectory = Path.Combine("artifacts", "visual-review", "03");
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

        return new GalleryCommandLineOptions(screenshotMode, theme, outputDirectory, sceneName);
    }

    public static string UsageText =>
        "Usage: dotnet run --project tools/NovelSpeaker.StyleGallery -- --screenshot --theme all --output artifacts/visual-review/03";
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
