using Wpf.Ui.Appearance;

namespace NovelSpeaker.App.Shared.Theming;

/// <summary>
/// Owns the application palette entry point without replacing resource dictionaries.
/// </summary>
internal static class NovelSpeakerPaletteRuntime
{
    private const string LightPaletteSource =
        "/NovelSpeaker;component/Shared/Theming/Palettes/Palette.Light.xaml";
    private const string DarkPaletteSource =
        "/NovelSpeaker;component/Shared/Theming/Palettes/Palette.Dark.xaml";

    public static ApplicationTheme? CurrentTheme { get; private set; }

    public static void Apply(ApplicationTheme theme)
    {
        CurrentTheme = theme;
        SemanticPaletteRuntime.Apply(
            global::System.Windows.Application.Current ??
                throw new InvalidOperationException("WPF Application is not initialized."),
            theme,
            LightPaletteSource,
            DarkPaletteSource);
    }

    public static void ApplySystemTheme()
    {
        CurrentTheme = null;
        var theme = ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;
        SemanticPaletteRuntime.Apply(
            global::System.Windows.Application.Current ??
                throw new InvalidOperationException("WPF Application is not initialized."),
            theme,
            LightPaletteSource,
            DarkPaletteSource);
    }
}
