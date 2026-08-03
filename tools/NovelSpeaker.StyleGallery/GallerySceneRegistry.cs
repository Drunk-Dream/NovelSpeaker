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
            "theme-resource-probe",
            "Dynamic theme resource probes for surfaces, text, borders and accent colors.",
            GallerySceneBuilders.CreateThemeResourceProbe),
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
