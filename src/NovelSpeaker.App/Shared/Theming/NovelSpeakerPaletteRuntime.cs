using Wpf.Ui.Appearance;

namespace NovelSpeaker.App.Shared.Theming;

/// <summary>
/// Owns the application palette entry point without replacing resource dictionaries.
/// Semantic palette values are added by the dedicated palette task; until then the
/// existing semantic brushes remain Wpf.Ui DynamicResource values.
/// </summary>
internal static class NovelSpeakerPaletteRuntime
{
    public static ApplicationTheme? CurrentTheme { get; private set; }

    public static void Apply(ApplicationTheme theme) => CurrentTheme = theme;

    public static void ApplySystemTheme() => CurrentTheme = null;
}
