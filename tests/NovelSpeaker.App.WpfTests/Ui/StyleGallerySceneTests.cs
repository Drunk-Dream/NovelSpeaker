using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NovelSpeaker.StyleGallery;
using NovelSpeaker.TestKit.Wpf;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class StyleGallerySceneTests
{
    [Fact]
    public void Scene_registry_groups_concrete_gallery_scenes_with_fixed_dimensions()
    {
        var scenes = GallerySceneRegistry.All;

        Assert.Equal(
            ["button-styles", "feedback", "form-field", "input-controls", "list-components", "media-controls", "menus", "navigation", "page-header", "palette-probe", "progress", "provider-controls", "provider-style-probe", "rules-shared", "section-surface", "selection", "settings-controls", "status-view", "surfaces", "theme-resource-probe", "token-components", "typography"],
            scenes.Select(scene => scene.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Theme foundations", "Standard controls", "Component families"],
            scenes.Select(scene => scene.GroupName).Distinct(StringComparer.Ordinal));
        Assert.Equal(
            ["provider-style-probe", "theme-resource-probe", "palette-probe", "token-components", "typography", "surfaces"],
            scenes.Where(scene => scene.Group == GallerySceneGroup.ThemeFoundations)
                .Select(scene => scene.Name));
        Assert.Equal(
            ["provider-controls", "button-styles", "input-controls", "selection", "navigation", "menus", "progress"],
            scenes.Where(scene => scene.Group == GallerySceneGroup.StandardControls)
                .Select(scene => scene.Name));
        Assert.Equal(
            ["media-controls", "list-components", "rules-shared", "feedback", "page-header", "section-surface", "status-view", "settings-controls", "form-field"],
            scenes.Where(scene => scene.Group == GallerySceneGroup.ComponentFamilies)
                .Select(scene => scene.Name));
        Assert.DoesNotContain(scenes, scene => scene.Name == "placeholder-sections");
        Assert.All(scenes, scene =>
        {
            Assert.Equal(GalleryRenderSettings.WindowWidth, scene.Width);
            Assert.Equal(GalleryRenderSettings.WindowHeight, scene.Height);
        });
        Assert.Equal(96, GalleryRenderSettings.Dpi);
    }

    [Fact]
    public void Gallery_window_scene_selector_exposes_the_three_scene_groups()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            var window = new GalleryWindow();
            var selector = FindDescendants<ComboBox>((DependencyObject)window.Content!).Single();
            var view = Assert.IsAssignableFrom<ICollectionView>(selector.ItemsSource);

            Assert.Equal(
                ["Theme foundations", "Standard controls", "Component families"],
                view.Groups.Cast<CollectionViewGroup>().Select(group => group.Name));
            Assert.Equal(22, view.Cast<GallerySceneDefinition>().Count());

            var headerTemplate = Assert.Single(selector.GroupStyle).HeaderTemplate;
            Assert.NotNull(headerTemplate);
            Assert.Equal(
                ["Theme foundations", "Standard controls", "Component families"],
                view.Groups.Cast<CollectionViewGroup>()
                    .Select(group =>
                    {
                        var headerHost = new ContentControl
                        {
                            Content = group,
                            ContentTemplate = headerTemplate!
                        };
                        headerHost.Measure(new Size(240, 32));
                        headerHost.Arrange(new Rect(0, 0, 240, 32));
                        headerHost.UpdateLayout();
                        return FindDescendants<WpfTextBlock>(headerHost).Single().Text;
                    }));
        });
    }

    [Fact]
    public void Gallery_screenshot_options_use_stable_scene_ids_and_family_outputs()
    {
        var defaultOptions = GalleryCommandLineOptions.Parse(["--screenshot"]);
        var sceneOptions = GalleryCommandLineOptions.Parse(
        [
            "--screenshot",
            "--scene",
            "button-styles",
            "--output",
            Path.Combine("artifacts", "visual-review", "gallery", "buttons")
        ]);

        Assert.Equal(
            Path.Combine("artifacts", "visual-review", "gallery"),
            defaultOptions.OutputDirectory);
        Assert.Null(defaultOptions.SceneName);
        Assert.Equal("button-styles", sceneOptions.SceneName);
        Assert.Equal(
            Path.Combine("artifacts", "visual-review", "gallery", "buttons"),
            sceneOptions.OutputDirectory);
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Every_scene_can_measure_arrange_and_render_without_dispatcher_exceptions(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            var dispatcherExceptions = new List<Exception>();
            DispatcherUnhandledExceptionEventHandler handler = (_, args) =>
            {
                dispatcherExceptions.Add(args.Exception);
                args.Handled = true;
            };
            Dispatcher.CurrentDispatcher.UnhandledException += handler;
            try
            {
                foreach (var scene in GallerySceneRegistry.All)
                {
                    var root = scene.Create();
                    Assert.Equal(scene.Name, AutomationProperties.GetAutomationId(root));

                    root.Measure(new Size(scene.Width, scene.Height));
                    root.Arrange(new Rect(0, 0, scene.Width, scene.Height));
                    root.UpdateLayout();

                    var bitmap = GallerySceneRenderer.Render(root, scene);

                    Assert.Equal(scene.Width, bitmap.PixelWidth);
                    Assert.Equal(scene.Height, bitmap.PixelHeight);
                    Assert.True(bitmap.DpiX > 0);
                    Assert.True(bitmap.DpiY > 0);
                    Assert.True(root.ActualWidth > 0);
                    Assert.True(root.ActualHeight > 0);
                }
            }
            finally
            {
                Dispatcher.CurrentDispatcher.UnhandledException -= handler;
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }

            Assert.Empty(dispatcherExceptions);
        });
    }

    [Fact]
    public async Task Screenshot_generator_is_explicitly_guarded_and_repeatable()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--scene",
                "button-styles",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                WpfWindowHost.Show(window);
                var first = await new GalleryScreenshotGenerator().GenerateAsync(
                    window,
                    options,
                    cancellation.Token);
                var firstSnapshot = first.Scenes
                    .Select(scene => $"{scene.Scene}|{scene.Theme}|{scene.Width}|{scene.Height}|{scene.Sha256}")
                    .ToArray();

                var second = await new GalleryScreenshotGenerator().GenerateAsync(
                    window,
                    options,
                    cancellation.Token);
                Assert.Equal(firstSnapshot, second.Scenes
                    .Select(scene => $"{scene.Scene}|{scene.Theme}|{scene.Width}|{scene.Height}|{scene.Sha256}")
                    .ToArray());
                Assert.All(second.Scenes, scene =>
                {
                    var bytes = File.ReadAllBytes(Path.Combine(output.Path, scene.Png));
                    Assert.Equal(
                        scene.Sha256,
                        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
                });

                using var manifest = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(output.Path, "manifest.json")));
                Assert.Equal("button-styles", manifest.RootElement.GetProperty("artifactId").GetString());
                Assert.Equal("NovelSpeaker.StyleGallery", manifest.RootElement.GetProperty("tool").GetString());
                Assert.Equal(GalleryRenderSettings.WindowWidth, manifest.RootElement.GetProperty("windowWidth").GetInt32());
                Assert.Equal(GalleryRenderSettings.WindowHeight, manifest.RootElement.GetProperty("windowHeight").GetInt32());
                Assert.Equal(GalleryRenderSettings.Dpi, manifest.RootElement.GetProperty("dpi").GetInt32());
                var manifestScenes = manifest.RootElement.GetProperty("scenes").EnumerateArray().ToArray();
                Assert.Equal(2, manifestScenes.Length);
                Assert.Equal(
                    ["Dark", "Light"],
                    manifestScenes.Select(scene => scene.GetProperty("theme").GetString()).Order(StringComparer.Ordinal));
                Assert.All(manifestScenes, scene =>
                {
                    Assert.Equal("button-styles", scene.GetProperty("scene").GetString());
                    Assert.Equal(GalleryRenderSettings.WindowWidth, scene.GetProperty("width").GetInt32());
                    Assert.Equal(GalleryRenderSettings.WindowHeight, scene.GetProperty("height").GetInt32());
                    Assert.Equal(GalleryRenderSettings.Dpi, scene.GetProperty("dpi").GetInt32());
                    var png = scene.GetProperty("png").GetString()!;
                    Assert.False(Path.IsPathRooted(png));
                    Assert.Equal(png, Path.GetFileName(png));
                    var bytes = File.ReadAllBytes(Path.Combine(output.Path, png));
                    Assert.Equal(
                        scene.GetProperty("sha256").GetString(),
                        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
                    using var stream = new MemoryStream(bytes, writable: false);
                    var frame = Assert.Single(
                        BitmapDecoder.Create(
                            stream,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad).Frames);
                    Assert.Equal(scene.GetProperty("width").GetInt32(), frame.PixelWidth);
                    Assert.Equal(scene.GetProperty("height").GetInt32(), frame.PixelHeight);
                    Assert.InRange(
                        frame.DpiX,
                        GalleryRenderSettings.Dpi - 0.1,
                        GalleryRenderSettings.Dpi + 0.1);
                    Assert.InRange(
                        frame.DpiY,
                        GalleryRenderSettings.Dpi - 0.1,
                        GalleryRenderSettings.Dpi + 0.1);
                });
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }

                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
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

    private sealed class TemporaryOutputDirectory : IDisposable
    {
        public TemporaryOutputDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "NovelSpeakerStyleGalleryTests",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
