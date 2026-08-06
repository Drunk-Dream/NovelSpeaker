using System.Windows;

namespace NovelSpeaker.StyleGallery;

public static class GallerySceneRegistry
{
    private static readonly IReadOnlyList<GallerySceneDefinition> RegisteredScenes =
    [
        new GallerySceneDefinition(
            "provider-style-probe",
            GallerySceneGroup.ThemeFoundations,
            "Explicit Provider Style Bridge aliases and measurable interaction contracts.",
            GallerySceneBuilders.CreateProviderStyleProbe),
        new GallerySceneDefinition(
            "theme-resource-probe",
            GallerySceneGroup.ThemeFoundations,
            "Dynamic theme resource probes for surfaces, text, borders and accent colors.",
            GallerySceneBuilders.CreateThemeResourceProbe),
        new GallerySceneDefinition(
            "palette-probe",
            GallerySceneGroup.ThemeFoundations,
            "Complete semantic palette with text and icon contrast samples.",
            GallerySceneBuilders.CreatePaletteProbe),
        new GallerySceneDefinition(
            "token-components",
            GallerySceneGroup.ThemeFoundations,
            "Stable token-based PageHeader, SectionSurface and StatusView samples.",
            GallerySceneBuilders.CreateTokenComponents),
        new GallerySceneDefinition(
            "typography",
            GallerySceneGroup.ThemeFoundations,
            "Typography roles with long Chinese and English text plus disabled and validation states.",
            GallerySceneBuilders.CreateTypographyStyles),
        new GallerySceneDefinition(
            "surfaces",
            GallerySceneGroup.ThemeFoundations,
            "Surface hierarchy, nested depth and raised transient surfaces across themes.",
            GallerySceneBuilders.CreateSurfaceStyles),
        new GallerySceneDefinition(
            "provider-controls",
            GallerySceneGroup.StandardControls,
            "Wpf.Ui provider-backed standard controls and interaction states.",
            GallerySceneBuilders.CreateProviderControls),
        new GallerySceneDefinition(
            "button-styles",
            GallerySceneGroup.StandardControls,
            "Explicit App.Button variants and deterministic interaction-state previews.",
            GallerySceneBuilders.CreateButtonStyles),
        new GallerySceneDefinition(
            "input-controls",
            GallerySceneGroup.StandardControls,
            "Explicit App.Input TextBox, PasswordBox, ComboBox, CheckBox and ToggleSwitch fixtures.",
            GallerySceneBuilders.CreateInputControls),
        new GallerySceneDefinition(
            "media-controls",
            GallerySceneGroup.ComponentFamilies,
            "Shared App.Button.Icon controls with deterministic playback and slider fixtures.",
            GallerySceneBuilders.CreateMediaControls),
        new GallerySceneDefinition(
            "list-components",
            GallerySceneGroup.ComponentFamilies,
            "Book cards, list rows, selection rows, settings rows, rule items and empty states.",
            GallerySceneBuilders.CreateListComponents),
        new GallerySceneDefinition(
            "navigation-feedback",
            GallerySceneGroup.ComponentFamilies,
            "Navigation entries, menus, progress, transient surfaces and request states.",
            GallerySceneBuilders.CreateNavigationFeedback)
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
