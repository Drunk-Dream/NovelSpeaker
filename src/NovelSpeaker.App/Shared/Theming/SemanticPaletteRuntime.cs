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
    private static readonly IReadOnlyDictionary<string, string> ProjectionAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NavigationViewContentBackground"] = "App.Brush.Canvas",
            ["NavigationViewContentGridBorderBrush"] = "App.Brush.Border.Subtle"
        };

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
        "App.Brush.Interaction.Surface.Hover",
        "App.Brush.Interaction.Surface.Pressed",
        "App.Brush.Interaction.Border.Hover",
        "App.Brush.Interaction.Border.Pressed",
        "App.Brush.Interaction.Foreground.Hover",
        "App.Brush.Interaction.Foreground.Pressed",
        "App.Brush.Interaction.Foreground.Selected",
        "App.Brush.Interaction.Foreground.Disabled",
        "App.Brush.Accent",
        "App.Brush.Accent.Default",
        "App.Brush.Accent.Hover",
        "App.Brush.Accent.Pressed",
        "App.Brush.Accent.Subtle",
        "App.Brush.Accent.Subtle.Hover",
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
        // Provider projections used by the NavigationView shell content host.
        "NavigationViewContentBackground",
        "NavigationViewContentGridBorderBrush"
    ];

    internal static void Apply(
        global::System.Windows.Application application,
        ApplicationTheme theme,
        string lightPaletteSource,
        string darkPaletteSource,
        string? highContrastPaletteSource = null,
        bool useHighContrast = false)
    {
        ArgumentNullException.ThrowIfNull(application);

        var paletteSource = useHighContrast
            ? highContrastPaletteSource ??
              throw new ArgumentNullException(nameof(highContrastPaletteSource))
            : theme == ApplicationTheme.Dark
                ? darkPaletteSource
                : lightPaletteSource;
        var palette = new ResourceDictionary
        {
            Source = new Uri(paletteSource, UriKind.Relative)
        };
        var paletteKeys = palette.Keys.Cast<object>().OfType<string>().Order(StringComparer.Ordinal).ToArray();
        var expectedKeys = Keys.Order(StringComparer.Ordinal).ToArray();
        if (!expectedKeys.SequenceEqual(paletteKeys, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Semantic palette keys do not match the stable palette contract. Expected: {string.Join(",", expectedKeys)}; actual: {string.Join(",", paletteKeys)}.");
        }

        foreach (var key in Keys)
        {
            if (palette[key] is not SolidColorBrush sourceBrush)
            {
                throw new InvalidOperationException($"Semantic palette resource '{key}' is not a SolidColorBrush.");
            }

            var targetDictionary = FindResourceDictionary(application, key);
            if (targetDictionary is null)
            {
                throw new InvalidOperationException($"Semantic palette resource '{key}' is not loaded.");
            }

            if (ProjectionAliases.TryGetValue(key, out var canonicalKey))
            {
                var canonicalDictionary = FindResourceDictionary(application, canonicalKey);
                if (canonicalDictionary?[canonicalKey] is not SolidColorBrush canonicalBrush)
                {
                    throw new InvalidOperationException(
                        $"Canonical semantic palette resource '{canonicalKey}' is not loaded.");
                }

                targetDictionary[key] = canonicalBrush;
                continue;
            }

            if (useHighContrast)
            {
                // Keep the source Freezable so its DynamicResource Color expression
                // continues to follow Windows system color changes.
                targetDictionary[key] = sourceBrush;
                continue;
            }

            if (targetDictionary[key] is SolidColorBrush targetBrush && !targetBrush.IsFrozen)
            {
                targetBrush.Color = sourceBrush.Color;
                continue;
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
