using System.Windows;

namespace NovelSpeaker.StyleGallery;

public static class GallerySceneRegistry
{
    private static readonly IReadOnlyList<GallerySceneDefinition> RegisteredScenes =
    [
        new GallerySceneDefinition(
            "provider-controls",
            "Wpf.Ui provider-backed standard controls and interaction states.",
            GallerySceneBuilders.CreateProviderControls),
        new GallerySceneDefinition(
            "provider-style-probe",
            "Explicit Provider Style Bridge aliases and measurable interaction contracts.",
            GallerySceneBuilders.CreateProviderStyleProbe),
        new GallerySceneDefinition(
            "theme-resource-probe",
            "Dynamic theme resource probes for surfaces, text, borders and accent colors.",
            GallerySceneBuilders.CreateThemeResourceProbe),
        new GallerySceneDefinition(
            "palette-probe",
            "Complete semantic palette with text and icon contrast samples.",
            GallerySceneBuilders.CreatePaletteProbe),
        new GallerySceneDefinition(
            "token-components",
            "Stable token-based PageHeader, SectionSurface and StatusView samples.",
            GallerySceneBuilders.CreateTokenComponents),
        new GallerySceneDefinition(
            "button-styles",
            "Explicit App.Button variants and deterministic interaction-state previews.",
            GallerySceneBuilders.CreateButtonStyles),
        new GallerySceneDefinition(
            "media-controls",
            "Shared App.Button.Icon controls with deterministic playback and slider fixtures.",
            GallerySceneBuilders.CreateMediaControls),
        new GallerySceneDefinition(
            "placeholder-sections",
            "Reserved sections for the later visual component waves.",
            GallerySceneBuilders.CreatePlaceholderSections)
    ];

    public static IReadOnlyList<GallerySceneDefinition> All => RegisteredScenes;

    public static FrameworkElement Build(string name)
    {
        var scene = RegisteredScenes.FirstOrDefault(
            candidate => candidate.Name.Equals(name, StringComparison.Ordinal));
        return scene?.Create()
            ?? throw new ArgumentException($"Unknown Style Gallery scene '{name}'.", nameof(name));
    }
}
