using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using NovelSpeaker.StyleGallery;
using Xunit;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class ListComponentStyleContractTests
{
    [Fact]
    public void Pseudo_public_component_sources_and_dictionary_are_removed()
    {
        var root = LocateRepositoryRoot();
        Assert.False(File.Exists(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Components",
            "AppComponentBase.cs")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Components",
            "FeedbackSurfaceBase.cs")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "ControlThemes",
            "ComponentStyles.xaml")));
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void List_components_scene_uses_formal_controls_and_gallery_owned_fixture_content(GalleryTheme theme)
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

            Assert.Equal(2, FindDescendants<AppSettingsRow>(scene).Count);
            Assert.Single(FindDescendants<AppSettingsNavigationRow>(scene));
            Assert.Single(FindDescendants<AppSettingsGroup>(scene));
            Assert.Single(FindDescendants<AppStatusView>(scene));

            var stateIds = FindDescendants<Border>(scene)
                .Select(border => AutomationProperties.GetAutomationId(border))
                .Where(id => id is not null &&
                             (id.StartsWith("book-card-", StringComparison.Ordinal) ||
                              id.StartsWith("list-row-", StringComparison.Ordinal) ||
                              id.StartsWith("selectable-row-", StringComparison.Ordinal)))
                .ToArray();
            Assert.Equal(27, stateIds.Length);
            Assert.Equal(27, stateIds.Distinct(StringComparer.Ordinal).Count());

            var card = FindDescendants<Border>(scene).Single(border =>
                AutomationProperties.GetAutomationId(border) == "book-card-selected");
            Assert.Same(card.Style?.BasedOn, scene.FindResource("App.Selection.CardItem"));
            Assert.True(card.ActualWidth > 0);
            Assert.True(card.ActualHeight > 0);

            var playing = FindDescendants<Border>(scene).Single(border =>
                AutomationProperties.GetAutomationId(border) == "list-row-playing");
            Assert.Same(playing.BorderBrush, scene.FindResource("App.Brush.Accent.Default"));

            var hover = FindDescendants<Border>(scene).Single(border =>
                AutomationProperties.GetAutomationId(border) == "selectable-row-hover");
            Assert.Same(hover.Background, scene.FindResource("App.Brush.Surface.Secondary"));

            var focus = FindDescendants<Border>(scene).Single(border =>
                AutomationProperties.GetAutomationId(border) == "selectable-row-focus");
            Assert.Same(focus.BorderBrush, scene.FindResource("App.Brush.Focus"));
            Assert.Equal(new Thickness(2), focus.BorderThickness);

            var disabled = FindDescendants<Border>(scene).Single(border =>
                AutomationProperties.GetAutomationId(border) == "book-card-disabled");
            Assert.False(disabled.IsEnabled);
            Assert.InRange(disabled.Opacity, 0, 0.99);

            var title = FindDescendants<WpfTextBlock>(card).Single(block =>
                AutomationProperties.GetAutomationId(block) == "book-card-title");
            Assert.Equal(TextWrapping.NoWrap, title.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
            Assert.Equal(title.Text, title.ToolTip);
            Assert.Equal(title.Text, AutomationProperties.GetName(title));

            var list = FindDescendants<ItemsControl>(scene).Single(control =>
                AutomationProperties.GetAutomationId(control) == "list-components-virtualized-host");
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
            Assert.False(list is Selector);
            Assert.Equal(
                "virtualized-selectable-row-03",
                AutomationProperties.GetAutomationId(
                    FindDescendants<Border>(list).Single(border =>
                        AutomationProperties.GetAutomationId(border) == "virtualized-selectable-row-03")));
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
