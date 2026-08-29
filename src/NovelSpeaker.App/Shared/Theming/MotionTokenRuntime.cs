using System.Windows;

namespace NovelSpeaker.App.Shared.Theming;

/// <summary>
/// Resolves shared motion tokens for the small number of code-behind interactions
/// that need a TimeSpan instead of a XAML Duration. The token dictionary remains
/// the only owner of the actual durations.
/// </summary>
internal static class MotionTokenRuntime
{
    public static TimeSpan Fast => Resolve("App.Motion.Fast");

    public static TimeSpan Standard => Resolve("App.Motion.Standard");

    public static TimeSpan Slow => Resolve("App.Motion.Slow");

    private static TimeSpan Resolve(string key)
    {
        if (global::System.Windows.Application.Current?.TryFindResource(key) is Duration duration)
        {
            return duration.TimeSpan;
        }

        throw new InvalidOperationException($"Motion token '{key}' is not loaded.");
    }
}
