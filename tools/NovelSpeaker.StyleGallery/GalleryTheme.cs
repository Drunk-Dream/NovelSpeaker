using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using System.Windows;
using NovelSpeaker.App.Shared.Theming;

namespace NovelSpeaker.StyleGallery;

public enum GalleryTheme
{
    Light,
    Dark
}

public static class GalleryThemeExtensions
{
    public static GalleryThemeChoice Parse(string value) =>
        value.Equals("dark", StringComparison.OrdinalIgnoreCase)
            ? GalleryThemeChoice.Dark
            : value.Equals("light", StringComparison.OrdinalIgnoreCase)
                ? GalleryThemeChoice.Light
                : value.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? GalleryThemeChoice.All
                : throw new GalleryUsageException($"Theme must be 'light', 'dark' or 'all', but was '{value}'.");

    public static GalleryTheme ToGalleryTheme(this GalleryThemeChoice theme) =>
        theme == GalleryThemeChoice.Dark ? GalleryTheme.Dark : GalleryTheme.Light;

    public static ApplicationTheme ToWpfUiTheme(this GalleryTheme theme) =>
        theme == GalleryTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

    public static string FileName(this GalleryTheme theme) => theme.ToString().ToLowerInvariant();
}

public static class GalleryThemeRuntime
{
    private static readonly string[] ProviderBridgeKeys =
    [
        "Provider.Button",
        "Provider.TextBox",
        "Provider.PasswordBox",
        "Provider.ComboBox",
        "Provider.CheckBox",
        "Provider.ToggleSwitch",
        "Provider.NavigationViewItem",
        "Provider.Slider"
    ];

    public static void EnsureProviderResources()
    {
        var application = System.Windows.Application.Current
            ?? throw new InvalidOperationException("Style Gallery resources require a WPF Application.");
        var dictionaries = application.Resources.MergedDictionaries;

        if (dictionaries.Count < 3 || !IsWpfUiThemeDictionary(dictionaries[0]))
        {
            throw new InvalidOperationException(
                "Style Gallery Wpf.Ui theme provider must remain at logical dictionary position 0.");
        }

        if (dictionaries[1] is not ControlsDictionary)
        {
            throw new InvalidOperationException(
                "Style Gallery Wpf.Ui ControlsDictionary must remain at logical dictionary position 1.");
        }

        var bridge = dictionaries[2];
        var bridgeKeys = bridge.Keys
            .Cast<object>()
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!IsProviderBridgeDictionary(bridge) ||
            !ProviderBridgeKeys.Order(StringComparer.Ordinal).SequenceEqual(bridgeKeys) ||
            ProviderBridgeKeys.Any(key => bridge[key] is not Style))
        {
            throw new InvalidOperationException(
                "Style Gallery ProviderStyleBridge must remain at logical dictionary position 2 with its stable alias keys.");
        }

        if (dictionaries.Count < 4 ||
            dictionaries[3].Source?.OriginalString?.EndsWith(
                "Palettes/Palette.Light.xaml",
                StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException(
                "Style Gallery semantic palette must remain at logical dictionary position 3.");
        }

        if (dictionaries.Count < 5 ||
            dictionaries[4].Source?.OriginalString?.EndsWith(
                "Resources/Tokens/DesignTokens.xaml",
                StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException(
                "Style Gallery DesignTokens must remain at logical dictionary position 4.");
        }

        var expectedApplicationSources = new[]
        {
            "Resources/Styles/Typography.xaml",
            "Resources/Styles/Inputs.xaml",
            "Resources/Styles/ButtonStyles.xaml",
            "Resources/Styles/SliderStyles.xaml",
            "Resources/ControlThemes/ComponentStyles.xaml",
            "Resources/ControlThemes/NavigationFeedbackStyles.xaml",
            "Resources/Legacy/LegacyStyles.xaml"
        };
        if (dictionaries.Count != 12 ||
            expectedApplicationSources.Select((suffix, index) =>
                dictionaries[index + 5].Source?.OriginalString?.EndsWith(
                    suffix,
                    StringComparison.OrdinalIgnoreCase) == true).Any(isMatch => !isMatch))
        {
            throw new InvalidOperationException(
                "Style Gallery application resources must load Styles, ControlThemes and Legacy exactly once in order, with Legacy last.");
        }
    }

    private static bool IsWpfUiThemeDictionary(ResourceDictionary dictionary)
    {
        if (dictionary is ThemesDictionary)
        {
            return true;
        }

        var source = dictionary.Source?.OriginalString?.Replace('\\', '/');
        return dictionary.GetType() == typeof(ResourceDictionary) &&
               source?.Contains("Wpf.Ui", StringComparison.OrdinalIgnoreCase) == true &&
               source.Contains("/Resources/Theme/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProviderBridgeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString?.Replace('\\', '/');
        return source?.EndsWith("/ProviderStyleBridge.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static void Apply(GalleryTheme theme)
    {
        EnsureProviderResources();
        ApplicationThemeManager.Apply(theme.ToWpfUiTheme());

        var application = System.Windows.Application.Current!;
        var sourcePrefix = IsStyleGalleryPalette(application)
            ? "/NovelSpeaker.StyleGallery;component/Resources/Palettes/Palette."
            : "/NovelSpeaker.App;component/Shared/Theming/Palettes/Palette.";
        SemanticPaletteRuntime.Apply(
            application,
            theme.ToWpfUiTheme(),
            $"{sourcePrefix}Light.xaml",
            $"{sourcePrefix}Dark.xaml");

        SetGalleryAliases(application);
    }

    private static bool IsStyleGalleryPalette(System.Windows.Application application) =>
        application.Resources.MergedDictionaries.Any(dictionary =>
            dictionary.Source?.OriginalString?.Contains(
                "NovelSpeaker.StyleGallery",
                StringComparison.OrdinalIgnoreCase) == true);

    private static void SetGalleryAliases(System.Windows.Application application)
    {
        foreach (var (alias, paletteKey) in new[]
                 {
                     ("GalleryCanvasBackgroundBrush", "CanvasSurfaceBrush"),
                     ("GallerySurfaceBrush", "PrimarySurfaceBrush"),
                     ("GalleryMutedSurfaceBrush", "SecondarySurfaceBrush"),
                     ("GalleryPrimaryTextBrush", "PrimaryTextBrush"),
                     ("GallerySecondaryTextBrush", "SecondaryTextBrush"),
                     ("GalleryTertiaryTextBrush", "TertiaryTextBrush"),
                     ("GalleryBorderBrush", "SubtleBorderBrush"),
                     ("GalleryAccentBrush", "AccentBrush"),
                     ("GalleryOnAccentTextBrush", "AccentTextBrush")
                 })
        {
            application.Resources[alias] = application.FindResource(paletteKey);
        }
    }
}
