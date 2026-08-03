using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovelSpeaker.StyleGallery;

public sealed class GalleryWindow : Window
{
    private readonly ComboBox _sceneSelector;
    private readonly ContentControl _sceneHost;

    public GalleryWindow()
    {
        Title = "NovelSpeaker Style Gallery";
        Width = GalleryRenderSettings.WindowWidth;
        Height = GalleryRenderSettings.WindowHeight;
        MinWidth = Width;
        MaxWidth = Width;
        MinHeight = Height;
        MaxHeight = Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _sceneSelector = new ComboBox
        {
            Width = 240,
            ItemsSource = GallerySceneRegistry.All,
            DisplayMemberPath = nameof(GallerySceneDefinition.Name),
            SelectedIndex = 0
        };
        AutomationProperties.SetName(_sceneSelector, "Style Gallery scene selector");
        _sceneSelector.SelectionChanged += OnSceneSelectionChanged;

        _sceneHost = new ContentControl();
        Content = CreateInteractiveContent();
    }

    public async Task<GalleryManifest> GenerateScreenshotsAsync(
        GalleryCommandLineOptions options,
        CancellationToken cancellationToken = default)
    {
        var generator = new GalleryScreenshotGenerator();
        var manifest = await generator.GenerateAsync(this, options, cancellationToken);
        Close();
        return manifest;
    }

    private Grid CreateInteractiveContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.SetResourceReference(Panel.BackgroundProperty, "GalleryCanvasBackgroundBrush");

        var toolbar = new DockPanel
        {
            LastChildFill = false,
            Margin = new Thickness(16, 8, 16, 8)
        };
        var title = new TextBlock
        {
            Text = "Style Gallery",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "GalleryPrimaryTextBrush");
        toolbar.Children.Add(title);

        var light = CreateThemeButton("Light", GalleryTheme.Light);
        DockPanel.SetDock(light, Dock.Right);
        toolbar.Children.Add(light);
        var dark = CreateThemeButton("Dark", GalleryTheme.Dark);
        DockPanel.SetDock(dark, Dock.Right);
        toolbar.Children.Add(dark);
        DockPanel.SetDock(_sceneSelector, Dock.Right);
        toolbar.Children.Add(_sceneSelector);
        root.Children.Add(toolbar);

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _sceneHost
        };
        Grid.SetRow(scrollViewer, 1);
        root.Children.Add(scrollViewer);
        SetSelectedScene();
        return root;
    }

    private Button CreateThemeButton(string label, GalleryTheme theme)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 64,
            MinHeight = 32,
            Margin = new Thickness(8, 0, 0, 0)
        };
        AutomationProperties.SetName(button, $"Apply {label} theme");
        button.Click += (_, _) => GalleryThemeRuntime.Apply(theme);
        return button;
    }

    private void OnSceneSelectionChanged(object sender, SelectionChangedEventArgs e) => SetSelectedScene();

    private void SetSelectedScene()
    {
        if (_sceneSelector.SelectedItem is GallerySceneDefinition scene)
        {
            _sceneHost.Content = scene.Create();
        }
    }
}
