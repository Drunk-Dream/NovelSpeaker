using System.Windows;

namespace NovelSpeaker.App.Shared.Theming;

internal enum ThemePaletteKind
{
    Light,
    Dark
}

internal interface IThemePaletteLoader
{
    ResourceDictionary Load(ThemePaletteKind palette);
}

internal sealed class PackThemePaletteLoader : IThemePaletteLoader
{
    private const string ResourcePrefix = "/NovelSpeaker.App;component/Shared/Theming/Resources/Themes/";

    public ResourceDictionary Load(ThemePaletteKind palette)
    {
        var resourceName = palette == ThemePaletteKind.Dark
            ? "Palette.Dark.xaml"
            : "Palette.Light.xaml";

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(ResourcePrefix + resourceName, UriKind.RelativeOrAbsolute)
        };

        ThemePaletteResourceKeys.Validate(dictionary);
        return dictionary;
    }
}

internal static class ThemePaletteResourceKeys
{
    private static readonly IReadOnlySet<string> RequiredKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "AppBackgroundBrush",
        "CanvasSurfaceBrush",
        "PrimarySurfaceBrush",
        "SecondarySurfaceBrush",
        "RaisedSurfaceBrush",
        "PrimaryTextBrush",
        "SecondaryTextBrush",
        "TertiaryTextBrush",
        "SubtleBorderBrush",
        "StrongBorderBrush",
        "AccentBrush",
        "AccentForegroundBrush",
        "DangerBrush",
        "WarningBrush",
        "SuccessBrush",
        "AccentHoverBrush",
        "AccentPressedBrush",
        "AccentSubtleBrush",
        "AccentSubtleHoverBrush",
        "AccentFocusRingBrush",
        "DangerSubtleBrush",
        "WarningSubtleBrush",
        "SuccessSubtleBrush"
    };

    internal static IReadOnlySet<string> SemanticKeys => RequiredKeys;

    internal static void Validate(ResourceDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        var actualKeys = GetKeys(dictionary);
        if (!actualKeys.SetEquals(RequiredKeys))
        {
            var missing = RequiredKeys.Except(actualKeys).OrderBy(static key => key).ToArray();
            var unexpected = actualKeys.Except(RequiredKeys).OrderBy(static key => key).ToArray();
            throw new InvalidOperationException(
                $"Theme palette resource keys are invalid. Missing: {string.Join(", ", missing)}. " +
                $"Unexpected: {string.Join(", ", unexpected)}.");
        }
    }

    internal static bool HasSameKeys(ResourceDictionary first, ResourceDictionary second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return GetKeys(first).SetEquals(GetKeys(second));
    }

    internal static bool IsValid(ResourceDictionary dictionary)
    {
        try
        {
            Validate(dictionary);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static HashSet<string> GetKeys(ResourceDictionary dictionary) =>
        dictionary.Keys
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
}

internal sealed record ThemePaletteApplyResult(
    bool IsApplied,
    ThemePaletteKind? EffectivePalette,
    bool UsedFallback);

internal sealed class ThemePaletteRuntime
{
    private const string ThemeResourcesSuffix = "/Shared/Theming/Resources/Themes/ThemeResources.xaml";

    private readonly ResourceDictionary _applicationResources;
    private readonly IThemePaletteLoader _paletteLoader;
    private ThemePaletteKind? _currentPalette;

    public ThemePaletteRuntime()
        : this(
            global::System.Windows.Application.Current?.Resources
                ?? throw new InvalidOperationException("Application resources are not available."),
            new PackThemePaletteLoader())
    {
    }

    internal ThemePaletteRuntime(
        ResourceDictionary applicationResources,
        IThemePaletteLoader paletteLoader)
    {
        _applicationResources = applicationResources ?? throw new ArgumentNullException(nameof(applicationResources));
        _paletteLoader = paletteLoader ?? throw new ArgumentNullException(nameof(paletteLoader));
    }

    internal ThemePaletteApplyResult Apply(ThemePaletteKind requestedPalette)
    {
        var light = TryLoad(ThemePaletteKind.Light);
        var dark = TryLoad(ThemePaletteKind.Dark);
        var palettesAreConsistent = light is not null &&
                                    dark is not null &&
                                    ThemePaletteResourceKeys.HasSameKeys(light, dark);

        ResourceDictionary? selected = null;
        var usedFallback = false;
        if (palettesAreConsistent)
        {
            selected = requestedPalette == ThemePaletteKind.Dark ? dark : light;
        }
        else if (light is not null)
        {
            selected = light;
            usedFallback = requestedPalette == ThemePaletteKind.Dark;
        }

        var themeResources = FindThemeResources();
        if (selected is null && themeResources is not null)
        {
            selected = FindValidPalette(themeResources);
            usedFallback = selected is not null;
        }

        if (selected is null)
        {
            return new ThemePaletteApplyResult(false, _currentPalette, true);
        }

        if (themeResources is null)
        {
            themeResources = new ResourceDictionary();
            _applicationResources.MergedDictionaries.Add(themeResources);
        }

        var entries = selected.Keys
            .Cast<object>()
            .Select(key => (Key: key, Value: selected[key]))
            .ToArray();
        themeResources.Clear();
        themeResources.MergedDictionaries.Clear();
        foreach (var entry in entries)
        {
            themeResources[entry.Key] = entry.Value;
        }
        _currentPalette = selected == dark ? ThemePaletteKind.Dark : ThemePaletteKind.Light;
        return new ThemePaletteApplyResult(true, _currentPalette, usedFallback);
    }

    private ResourceDictionary? TryLoad(ThemePaletteKind palette)
    {
        try
        {
            var dictionary = _paletteLoader.Load(palette);
            ThemePaletteResourceKeys.Validate(dictionary);
            return dictionary;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ResourceDictionary? FindThemeResources() =>
        _applicationResources.MergedDictionaries.FirstOrDefault(
            static dictionary => dictionary.Source?.OriginalString.EndsWith(
                ThemeResourcesSuffix,
                StringComparison.OrdinalIgnoreCase) == true);

    private static ResourceDictionary? FindValidPalette(ResourceDictionary themeResources)
    {
        if (ThemePaletteResourceKeys.IsValid(themeResources))
        {
            return themeResources;
        }

        return themeResources.MergedDictionaries.LastOrDefault(ThemePaletteResourceKeys.IsValid);
    }
}
