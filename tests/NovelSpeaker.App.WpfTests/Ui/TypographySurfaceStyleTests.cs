using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Xml.Linq;
using NovelSpeaker.StyleGallery;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class TypographySurfaceStyleTests
{
    private static readonly string[] TypographyKeys =
    [
        "App.Typography.PageTitle",
        "App.Typography.SectionTitle",
        "App.Typography.ItemTitle",
        "App.Typography.Body",
        "App.Typography.Secondary",
        "App.Typography.Caption",
        "App.Typography.FormLabel",
        "App.Typography.Validation"
    ];

    private static readonly string[] SurfaceKeys =
    [
        "App.Surface.Canvas",
        "App.Surface.Section",
        "App.Surface.Card",
        "App.Surface.Secondary",
        "App.Surface.Raised",
        "App.Surface.Popup",
        "App.Surface.DialogContent"
    ];

    [Fact]
    public void Typography_and_surface_keys_have_single_responsible_dictionaries()
    {
        var root = LocateRepositoryRoot();
        var typographyPath = Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Typography.xaml");
        var surfacePath = Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Surfaces.xaml");
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var typography = XDocument.Load(typographyPath).Root?.Elements().ToArray() ?? [];
        var surfaces = XDocument.Load(surfacePath).Root?.Elements().ToArray() ?? [];

        Assert.Equal(
            TypographyKeys,
            typography.Select(resource => (string?)resource.Attribute(xaml + "Key")).ToArray());
        Assert.Equal(
            SurfaceKeys,
            surfaces.Select(resource => (string?)resource.Attribute(xaml + "Key")).ToArray());
        Assert.All(typography, resource =>
        {
            Assert.Equal("Style", resource.Name.LocalName);
            Assert.Equal("TextBlock", resource.Attribute("TargetType")?.Value);
            Assert.DoesNotContain(resource.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        });
        Assert.All(surfaces, resource =>
        {
            Assert.Equal("Style", resource.Name.LocalName);
            Assert.Equal("Border", resource.Attribute("TargetType")?.Value);
            Assert.DoesNotContain(resource.Descendants(), element =>
                element.Name.LocalName == "TextBlock" ||
                element.Name.LocalName == "Button" ||
                element.Name.LocalName == "ContentControl");
        });
    }

    [Fact]
    public void Application_loads_typography_then_surface_styles_before_control_families()
    {
        WpfTestHost.RunInSta(() =>
        {
            var dictionaries = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current).Resources.MergedDictionaries;
            Assert.EndsWith(
                "Shared/Theming/Resources/Styles/Typography.xaml",
                dictionaries[8].Source?.OriginalString,
                StringComparison.Ordinal);
            Assert.EndsWith(
                "Shared/Theming/Resources/Styles/Surfaces.xaml",
                dictionaries[9].Source?.OriginalString,
                StringComparison.Ordinal);
            Assert.EndsWith(
                "Shared/Theming/Resources/Styles/Inputs.xaml",
                dictionaries[10].Source?.OriginalString,
                StringComparison.Ordinal);

            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            Assert.All(TypographyKeys, key => Assert.IsType<Style>(application.FindResource(key)));
            Assert.All(SurfaceKeys, key => Assert.IsType<Style>(application.FindResource(key)));
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Typography_gallery_covers_long_text_disabled_state_and_nonzero_layout(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("typography");
            using var host = new FixtureWindow(scene);
            host.ShowWindow();

            var blocks = FindDescendants<TextBlock>(scene)
                .Where(block => AutomationProperties.GetAutomationId(block).StartsWith(
                    "typography-",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(10, blocks.Length);
            Assert.Contains(blocks, block => block.Text.Contains("Long English heading", StringComparison.Ordinal));
            Assert.Contains(blocks, block => block.Text.Contains("长中文", StringComparison.Ordinal));
            Assert.All(blocks, block =>
            {
                Assert.NotNull(block.Style);
                Assert.True(block.ActualWidth > 0);
                Assert.True(block.ActualHeight > 0);
                Assert.Equal(TextWrapping.Wrap, block.TextWrapping);
            });

            var disabled = blocks.Single(block =>
                AutomationProperties.GetAutomationId(block) == "typography-disabled");
            Assert.False(disabled.IsEnabled);
            Assert.Equal(0.5, disabled.Opacity);
        });
    }

    [Fact]
    public void Surface_gallery_keeps_nested_hierarchy_to_three_levels_and_exposes_all_variants()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("surfaces");
            using var host = new FixtureWindow(scene);
            host.ShowWindow();

            var surfaces = FindDescendants<Border>(scene)
                .Where(border => AutomationProperties.GetAutomationId(border).StartsWith(
                    "surface-",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(9, surfaces.Length);
            Assert.All(surfaces, surface =>
            {
                Assert.NotNull(surface.Style);
                Assert.True(surface.ActualWidth > 0);
                Assert.True(surface.ActualHeight > 0);
                Assert.NotNull(surface.Background);
            });

            var nested = new[]
            {
                "surface-nested-level-1",
                "surface-nested-level-2",
                "surface-nested-level-3"
            };
            Assert.Equal(
                3,
                nested.Select(id => FindDescendants<Border>(scene).Single(border =>
                        AutomationProperties.GetAutomationId(border) == id))
                    .Max(GetSurfaceDepth));

            var raised = surfaces.Single(surface =>
                AutomationProperties.GetAutomationId(surface) == "surface-raised");
            var popup = surfaces.Single(surface =>
                AutomationProperties.GetAutomationId(surface) == "surface-popup");
            var dialog = surfaces.Single(surface =>
                AutomationProperties.GetAutomationId(surface) == "surface-dialog-content");
            Assert.IsType<DropShadowEffect>(raised.Effect);
            Assert.IsType<DropShadowEffect>(popup.Effect);
            Assert.IsType<DropShadowEffect>(dialog.Effect);
            Assert.True(((DropShadowEffect)dialog.Effect).BlurRadius > ((DropShadowEffect)popup.Effect).BlurRadius);
        });
    }

    [Fact]
    public void Surface_backgrounds_change_with_theme_without_replacing_style_instances()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("surfaces");
            using var host = new FixtureWindow(scene);
            host.ShowWindow();
            var canvas = FindDescendants<Border>(scene).Single(border =>
                AutomationProperties.GetAutomationId(border) == "surface-canvas");
            var style = canvas.Style;
            var lightColor = Assert.IsType<SolidColorBrush>(canvas.Background).Color;

            GalleryThemeRuntime.Apply(GalleryTheme.Dark);
            host.UpdateLayout();

            Assert.Same(style, canvas.Style);
            Assert.NotEqual(lightColor, Assert.IsType<SolidColorBrush>(canvas.Background).Color);
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
        });
    }

    private static int GetSurfaceDepth(Border surface)
    {
        var depth = 1;
        for (DependencyObject? current = surface; current is Border border && border.Child is Border child; current = child)
        {
            depth++;
        }

        return depth;
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

    private sealed class FixtureWindow : IDisposable
    {
        private readonly Window _window;

        public FixtureWindow(FrameworkElement content)
        {
            _window = new Window
            {
                Content = content,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
        }

        public void ShowWindow()
        {
            WpfWindowHost.Show(_window);
            _window.UpdateLayout();
        }

        public void UpdateLayout() => _window.UpdateLayout();

        public void Dispose()
        {
            if (_window.IsVisible)
            {
                _window.Close();
            }
        }
    }
}
