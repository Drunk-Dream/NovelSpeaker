using System.Windows;
using System.ComponentModel;
using System.Windows.Threading;
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
    private const string HighContrastPaletteSource =
        "/NovelSpeaker;component/Shared/Theming/Palettes/Palette.HighContrast.xaml";

    private static readonly HashSet<string> HighContrastSystemProperties =
    [
        "HighContrast",
        "WindowColor",
        "WindowTextColor",
        "ControlColor",
        "HighlightColor",
        "HighlightTextColor",
        "GrayTextColor"
    ];

    static NovelSpeakerPaletteRuntime()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public static ApplicationTheme? CurrentTheme { get; private set; }

    public static void Apply(ApplicationTheme theme)
    {
        CurrentTheme = theme;
        ApplyCurrentPalette();
    }

    public static void ApplySystemTheme()
    {
        CurrentTheme = null;
        ApplyCurrentPalette();
    }

    private static void OnSystemParametersChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        HandleSystemParametersChanged(args);
    }

    internal static void HandleSystemParametersChanged(PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not null &&
            !HighContrastSystemProperties.Contains(args.PropertyName))
        {
            return;
        }

        var application = global::System.Windows.Application.Current;
        if (application is null ||
            application.Dispatcher.HasShutdownStarted ||
            application.Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess())
        {
            ApplyCurrentPalette();
            return;
        }

        try
        {
            application.Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() =>
                {
                    if (application.Dispatcher.HasShutdownStarted ||
                        application.Dispatcher.HasShutdownFinished)
                    {
                        return;
                    }

                    ApplyCurrentPalette();
                }));
        }
        catch (InvalidOperationException) when (
            application.Dispatcher.HasShutdownStarted ||
            application.Dispatcher.HasShutdownFinished)
        {
            // The system settings event can race application shutdown.
        }
    }

    private static void ApplyCurrentPalette()
    {
        var application = global::System.Windows.Application.Current;
        if (application is null ||
            (application.Dispatcher.CheckAccess() is false))
        {
            return;
        }

        var theme = CurrentTheme ?? (ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light);
        SemanticPaletteRuntime.Apply(
            application,
            theme,
            LightPaletteSource,
            DarkPaletteSource,
            HighContrastPaletteSource,
            SystemParameters.HighContrast);
    }
}
