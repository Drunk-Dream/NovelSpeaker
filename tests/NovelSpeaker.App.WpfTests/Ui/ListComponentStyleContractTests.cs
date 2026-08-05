using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using NovelSpeaker.App.Shared.Theming.Components;
using NovelSpeaker.StyleGallery;
using Xunit;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class ListComponentStyleContractTests
{
    private static readonly Type[] ComponentTypes =
    [
        typeof(BookCard),
        typeof(ListRow),
        typeof(SelectableRow),
        typeof(SettingsRow),
        typeof(RuleListItem),
        typeof(EmptyState)
    ];

    private static readonly string[] ComponentStyleKeys =
    [
        "App.Component.BookCard",
        "App.Component.ListRow",
        "App.Component.SelectableRow",
        "App.Component.SettingsRow",
        "App.Component.RuleListItem",
        "App.Component.EmptyState"
    ];

    [Fact]
    public void Component_styles_are_named_custom_templates_without_page_width_ownership()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "ComponentStyles.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = document.Root?.Elements().ToArray() ?? [];

        Assert.Equal(
            ["App.Component.Base", .. ComponentStyleKeys],
            resources
                .Select(resource => resource.Attribute(xaml + "Key")?.Value ?? string.Empty)
                .ToArray());
        Assert.All(resources, resource => Assert.Equal("Style", resource.Name.LocalName));
        Assert.DoesNotContain(
            resources.SelectMany(resource => resource.Elements("Setter")),
            setter => (string?)setter.Attribute("Property") == "Width" ||
                       (string?)setter.Attribute("Property") == "Height");
        Assert.All(
            resources.Skip(1),
            resource => Assert.Equal(
                "{StaticResource App.Component.Base}",
                (string?)resource.Attribute("BasedOn")));
    }

    [Fact]
    public void Components_own_default_content_without_scene_content_injection()
    {
        WpfTestHost.RunInSta(() =>
        {
            var components = new AppComponentBase[]
            {
                new BookCard(),
                new ListRow(),
                new SelectableRow(),
                new SettingsRow(),
                new RuleListItem(),
                new EmptyState()
            };

            Assert.All(components, component =>
            {
                Assert.NotNull(component.Content);
                Assert.NotEmpty(VisualTreeChildren(component.Content));
            });
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void List_component_gallery_contains_all_components_states_and_accessible_long_content(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("list-components");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            host.Window.UpdateLayout();
            var components = FindDescendants<AppComponentBase>(scene)
                .Where(component => !AutomationProperties.GetAutomationId(component).StartsWith(
                    "virtualized-",
                    StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(54, components.Length);
            foreach (var type in ComponentTypes)
            {
                var typed = components.Where(component => component.GetType() == type).ToArray();
                Assert.Equal(9, typed.Length);
                Assert.All(
                    typed,
                    component =>
                    {
                        Assert.NotNull(component.Style);
                        Assert.NotNull(component.Template);
                        Assert.True(component.ActualWidth > 0);
                        Assert.True(component.ActualHeight > 0);
                        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(component)));
                        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(component)));
                        Assert.NotNull(component.ToolTip);
                    });
            }

            var selected = FindComponent(components.Where(component => component is BookCard), "selected");
            Assert.True(selected.IsSelected);
            Assert.False(selected.IsCurrentPlayback);
            Assert.False(selected.IsHoverPreview);
            Assert.False(selected.IsFocusPreview);

            var playing = FindComponent(components.Where(component => component is BookCard), "playing");
            Assert.False(playing.IsSelected);
            Assert.True(playing.IsCurrentPlayback);

            var selectedPlaying = FindComponent(components.Where(component => component is BookCard), "selected-playing");
            Assert.True(selectedPlaying.IsSelected);
            Assert.True(selectedPlaying.IsCurrentPlayback);

            var selectedHover = FindComponent(components.Where(component => component is BookCard), "selected-hover");
            Assert.True(selectedHover.IsSelected);
            Assert.True(selectedHover.IsHoverPreview);
            Assert.False(selectedHover.IsCurrentPlayback);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsAssignableFrom<FrameworkElement>(
                    selectedHover.Template!.FindName("HoverMarker", selectedHover)).Visibility);

            var playingHover = FindComponent(components.Where(component => component is BookCard), "playing-hover");
            Assert.False(playingHover.IsSelected);
            Assert.True(playingHover.IsCurrentPlayback);
            Assert.True(playingHover.IsHoverPreview);

            var hover = FindComponent(components.Where(component => component is BookCard), "hover");
            Assert.True(hover.IsHoverPreview);
            var focus = FindComponent(components.Where(component => component is BookCard), "focus");
            Assert.True(focus.IsFocusPreview);

            var disabled = FindComponent(components.Where(component => component is BookCard), "disabled");
            Assert.False(disabled.IsEnabled);
            Assert.InRange(disabled.Opacity, 0, 0.99);
            Assert.True(disabled.ActualWidth > 0);
            Assert.True(disabled.ActualHeight > 0);

            var bookTitle = FindDescendants<WpfTextBlock>(
                    components.Single(component =>
                        AutomationProperties.GetAutomationId(component) == "book-card-default"))
                .Single(block => AutomationProperties.GetAutomationId(block) == "book-card-title");
            Assert.Equal(TextWrapping.NoWrap, bookTitle.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, bookTitle.TextTrimming);
            Assert.Equal(bookTitle.Text, bookTitle.ToolTip);
            Assert.Equal(bookTitle.Text, AutomationProperties.GetName(bookTitle));
        });
    }

    [Fact]
    public void Component_state_markers_are_independent_and_virtualized_selection_is_external()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("list-components");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            var selected = FindComponent(
                FindDescendants<AppComponentBase>(scene)
                    .Where(component => !AutomationProperties.GetAutomationId(component).StartsWith(
                        "virtualized-",
                        StringComparison.Ordinal) && component is BookCard),
                "selected");
            var selectedMarker = Assert.IsAssignableFrom<FrameworkElement>(
                selected.Template!.FindName("SelectedMarker", selected));
            var selectedPlaybackMarker = Assert.IsAssignableFrom<FrameworkElement>(
                selected.Template.FindName("PlaybackMarker", selected));
            Assert.Equal(Visibility.Visible, selectedMarker.Visibility);
            Assert.Equal(Visibility.Collapsed, selectedPlaybackMarker.Visibility);

            var selectedPlaying = FindComponent(
                FindDescendants<AppComponentBase>(scene)
                    .Where(component => !AutomationProperties.GetAutomationId(component).StartsWith(
                        "virtualized-",
                        StringComparison.Ordinal) && component is BookCard),
                "selected-playing");
            Assert.Equal(
                Visibility.Visible,
                Assert.IsAssignableFrom<FrameworkElement>(
                    selectedPlaying.Template!.FindName("SelectedMarker", selectedPlaying)).Visibility);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsAssignableFrom<FrameworkElement>(
                    selectedPlaying.Template.FindName("PlaybackMarker", selectedPlaying)).Visibility);

            var selectedHover = FindComponent(
                FindDescendants<AppComponentBase>(scene)
                    .Where(component => !AutomationProperties.GetAutomationId(component).StartsWith(
                        "virtualized-",
                        StringComparison.Ordinal) && component is BookCard),
                "selected-hover");
            Assert.Equal(
                Visibility.Visible,
                Assert.IsAssignableFrom<FrameworkElement>(
                    selectedHover.Template!.FindName("HoverMarker", selectedHover)).Visibility);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsAssignableFrom<FrameworkElement>(
                    selectedHover.Template.FindName("SelectedMarker", selectedHover)).Visibility);

            var playingHover = FindComponent(
                FindDescendants<AppComponentBase>(scene)
                    .Where(component => !AutomationProperties.GetAutomationId(component).StartsWith(
                        "virtualized-",
                        StringComparison.Ordinal) && component is BookCard),
                "playing-hover");
            Assert.Equal(
                Visibility.Visible,
                Assert.IsAssignableFrom<FrameworkElement>(
                    playingHover.Template!.FindName("HoverMarker", playingHover)).Visibility);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsAssignableFrom<FrameworkElement>(
                    playingHover.Template.FindName("PlaybackMarker", playingHover)).Visibility);

            var list = FindDescendants<ItemsControl>(scene).Single(itemsControl =>
                AutomationProperties.GetAutomationId(itemsControl) == "list-components-virtualized-host");
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
            Assert.False(list is Selector);

            var virtualizedSelected = FindDescendants<SelectableRow>(list).Single(row =>
                AutomationProperties.GetAutomationId(row) == "virtualized-selectable-row-03");
            Assert.True(virtualizedSelected.IsSelected);
            Assert.True(virtualizedSelected.ActualWidth > 0);
        });
    }

    [Fact]
    public void List_components_keep_style_and_template_instances_when_theme_changes()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("list-components");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            var card = FindDescendants<BookCard>(scene).Single(component =>
                AutomationProperties.GetAutomationId(component) == "book-card-selected");
            var style = card.Style;
            var template = card.Template;
            var surface = Assert.IsType<Border>(template!.FindName("Surface", card));
            var lightColor = Assert.IsType<SolidColorBrush>(surface.Background).Color;

            GalleryThemeRuntime.Apply(GalleryTheme.Dark);
            host.Window.UpdateLayout();

            Assert.Same(style, card.Style);
            Assert.Same(template, card.Template);
            Assert.NotEqual(lightColor, Assert.IsType<SolidColorBrush>(surface.Background).Color);
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
        });
    }

    private static AppComponentBase FindComponent(
        IEnumerable<AppComponentBase> components,
        string state)
    {
        return components.Single(component =>
        {
            var automationId = AutomationProperties.GetAutomationId(component);
            return state == "playing"
                ? automationId.EndsWith("-playing", StringComparison.Ordinal) &&
                  !automationId.EndsWith("-selected-playing", StringComparison.Ordinal)
                : state == "hover"
                    ? automationId.EndsWith("-hover", StringComparison.Ordinal) &&
                      !automationId.EndsWith("-selected-hover", StringComparison.Ordinal) &&
                      !automationId.EndsWith("-playing-hover", StringComparison.Ordinal)
                : automationId.EndsWith($"-{state}", StringComparison.Ordinal);
        });
    }

    private static IReadOnlyList<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var matches = new List<T>();
        Visit(root, matches);
        return matches;

        static void Visit(DependencyObject current, ICollection<T> matches)
        {
            if (current is T match)
            {
                matches.Add(match);
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                Visit(VisualTreeHelper.GetChild(current, index), matches);
            }
        }
    }

    private static IReadOnlyList<DependencyObject> VisualTreeChildren(object content) =>
        content is DependencyObject dependencyObject
            ? Enumerable
                .Range(0, VisualTreeHelper.GetChildrenCount(dependencyObject))
                .Select(index => VisualTreeHelper.GetChild(dependencyObject, index))
                .ToArray()
            : [];

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
