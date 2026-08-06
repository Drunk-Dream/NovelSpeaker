using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace NovelSpeaker.App.Shared.Theming;

/// <summary>
/// Applies palette values in the loaded semantic dictionary. Mutable brushes are
/// updated in place; frozen XAML brushes are replaced only at their palette keys.
/// Style and template dictionaries are intentionally outside this operation.
/// </summary>
internal static class SemanticPaletteRuntime
{
    internal static readonly IReadOnlyList<string> Keys =
    [
        "App.Brush.Window.Background",
        "App.Brush.Canvas",
        "App.Brush.Surface.Primary",
        "App.Brush.Surface.Secondary",
        "App.Brush.Surface.Raised",
        "App.Brush.Text.Primary",
        "App.Brush.Text.Secondary",
        "App.Brush.Text.Tertiary",
        "App.Brush.Border.Subtle",
        "App.Brush.Border.Strong",
        "App.Brush.Accent",
        "App.Brush.Accent.Default",
        "App.Brush.Accent.Hover",
        "App.Brush.Accent.Pressed",
        "App.Brush.Accent.Subtle",
        "App.Brush.Accent.Text",
        "App.Brush.Focus",
        "App.Brush.Danger",
        "App.Brush.Danger.Subtle",
        "App.Brush.Danger.Text",
        "App.Brush.Danger.Pressed",
        "App.Brush.Danger.Pressed.Text",
        "App.Brush.Warning",
        "App.Brush.Warning.Subtle",
        "App.Brush.Warning.Text",
        "App.Brush.Success",
        "App.Brush.Success.Subtle",
        "App.Brush.Success.Text",
        // Migration-compat keys kept for pages that have not been migrated yet.
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
        "AccentDefaultBrush",
        "AccentHoverBrush",
        "AccentPressedBrush",
        "AccentSubtleBrush",
        "AccentFocusRingBrush",
        "AccentTextBrush",
        "DangerBrush",
        "DangerSubtleBrush",
        "DangerTextBrush",
        "DangerPressedBrush",
        "DangerPressedTextBrush",
        "WarningBrush",
        "WarningSubtleBrush",
        "WarningTextBrush",
        "SuccessBrush",
        "SuccessSubtleBrush",
        "SuccessTextBrush"
    ];

    internal static void Apply(
        global::System.Windows.Application application,
        ApplicationTheme theme,
        string lightPaletteSource,
        string darkPaletteSource)
    {
        ArgumentNullException.ThrowIfNull(application);

        var palette = new ResourceDictionary
        {
            Source = new Uri(
                theme == ApplicationTheme.Dark ? darkPaletteSource : lightPaletteSource,
                UriKind.Relative)
        };
        var paletteKeys = palette.Keys.Cast<object>().OfType<string>().Order(StringComparer.Ordinal).ToArray();
        var expectedKeys = Keys.Order(StringComparer.Ordinal).ToArray();
        if (!expectedKeys.SequenceEqual(paletteKeys, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Semantic palette keys do not match the stable palette contract.");
        }

        foreach (var key in Keys)
        {
            if (palette[key] is not SolidColorBrush sourceBrush)
            {
                throw new InvalidOperationException($"Semantic palette resource '{key}' is not a SolidColorBrush.");
            }

            var targetDictionary = FindResourceDictionary(application, key);
            if (targetDictionary?[key] is SolidColorBrush targetBrush && !targetBrush.IsFrozen)
            {
                targetBrush.Color = sourceBrush.Color;
                continue;
            }

            if (targetDictionary is null)
            {
                throw new InvalidOperationException($"Semantic palette resource '{key}' is not loaded.");
            }

            targetDictionary[key] = new SolidColorBrush(sourceBrush.Color);
        }
    }

    private static ResourceDictionary? FindResourceDictionary(
        global::System.Windows.Application application,
        string key)
    {
        var mergedDictionary = application.Resources.MergedDictionaries
            .FirstOrDefault(dictionary =>
            {
                var source = dictionary.Source?.OriginalString?.Replace('\\', '/');
                return source?.EndsWith("/Palette.Light.xaml", StringComparison.OrdinalIgnoreCase) == true &&
                       dictionary.Contains(key);
            });
        mergedDictionary ??= application.Resources.MergedDictionaries
            .FirstOrDefault(dictionary => dictionary.Contains(key));
        if (mergedDictionary is not null)
        {
            return mergedDictionary;
        }

        return application.Resources.Contains(key) ? application.Resources : null;
    }
}
