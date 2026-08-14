using System.Windows;
using System.Windows.Automation;

namespace NovelSpeaker.StyleGallery;

public sealed class GallerySceneDefinition
{
    private readonly Func<FrameworkElement> _factory;

    public GallerySceneDefinition(
        string name,
        GallerySceneGroup group,
        string description,
        Func<FrameworkElement> factory)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A gallery scene must have a name.", nameof(name));
        }

        Name = name;
        Group = group;
        Description = description;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Width = GalleryRenderSettings.WindowWidth;
        Height = GalleryRenderSettings.WindowHeight;
    }

    public string Name { get; }

    public GallerySceneGroup Group { get; }

    public int GroupOrder => (int)Group;

    public string GroupName => Group switch
    {
        GallerySceneGroup.ThemeFoundations => "Theme foundations",
        GallerySceneGroup.StandardControls => "Standard controls",
        GallerySceneGroup.ComponentFamilies => "Component families",
        _ => throw new ArgumentOutOfRangeException()
    };

    public string Description { get; }

    public int Width { get; }

    public int Height { get; }

    public FrameworkElement Create()
    {
        var root = _factory();
        root.Width = Width;
        root.Height = Height;
        root.MinWidth = Width;
        root.MinHeight = Height;
        AutomationProperties.SetAutomationId(root, Name);
        return root;
    }
}

public enum GallerySceneGroup
{
    ThemeFoundations,
    StandardControls,
    ComponentFamilies
}
