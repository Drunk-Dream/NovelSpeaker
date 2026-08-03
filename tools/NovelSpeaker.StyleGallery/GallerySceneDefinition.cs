using System.Windows;
using System.Windows.Automation;

namespace NovelSpeaker.StyleGallery;

public sealed class GallerySceneDefinition
{
    private readonly Func<FrameworkElement> _factory;

    public GallerySceneDefinition(string name, string description, Func<FrameworkElement> factory)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A gallery scene must have a name.", nameof(name));
        }

        Name = name;
        Description = description;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Width = GalleryRenderSettings.WindowWidth;
        Height = GalleryRenderSettings.WindowHeight;
    }

    public string Name { get; }

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
