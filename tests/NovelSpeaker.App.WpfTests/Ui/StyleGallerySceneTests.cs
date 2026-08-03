using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using NovelSpeaker.StyleGallery;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class StyleGallerySceneTests
{
    [Fact]
    public void Scene_registry_contains_the_initial_gallery_scenes_with_fixed_dimensions()
    {
        var scenes = GallerySceneRegistry.All;

        Assert.Equal(
            ["placeholder-sections", "provider-controls", "theme-resource-probe"],
            scenes.Select(scene => scene.Name).Order(StringComparer.Ordinal));
        Assert.All(scenes, scene =>
        {
            Assert.Equal(GalleryRenderSettings.WindowWidth, scene.Width);
            Assert.Equal(GalleryRenderSettings.WindowHeight, scene.Height);
        });
        Assert.Equal(96, GalleryRenderSettings.Dpi);
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
            }

            Assert.Empty(dispatcherExceptions);
        });
    }

    [Fact]
    public void Provider_scene_contains_provider_controls_and_theme_probe_has_dynamic_resources()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            var providerScene = GallerySceneRegistry.Build("provider-controls");
            var probeScene = GallerySceneRegistry.Build("theme-resource-probe");

            Assert.NotEmpty(FindDescendants<WpfButton>(providerScene));
            Assert.NotEmpty(FindDescendants<WpfTextBox>(providerScene));
            Assert.NotEmpty(FindDescendants<ComboBox>(providerScene));
            Assert.NotEmpty(FindDescendants<ToggleSwitch>(providerScene));
            Assert.NotEmpty(FindDescendants<Slider>(providerScene));
            Assert.NotEmpty(FindDescendants<ProgressBar>(providerScene));

            var dynamicResourceElements = FindDescendants<FrameworkElement>(probeScene)
                .Where(element => element.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue ||
                                  element.ReadLocalValue(Control.ForegroundProperty) != DependencyProperty.UnsetValue)
                .ToArray();
            Assert.NotEmpty(dynamicResourceElements);
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_verified_manifest_and_stable_png_outputs()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                window.Show();
                var generator = new GalleryScreenshotGenerator();

                await generator.GenerateAsync(window, options, cancellation.Token);
                var firstManifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);
                await AssertManifestMatchesPngsAsync(firstManifest, output.Path, cancellation.Token);
                var firstSnapshot = CreateSceneSnapshot(firstManifest);

                await generator.GenerateAsync(window, options, cancellation.Token);
                var secondManifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);
                await AssertManifestMatchesPngsAsync(secondManifest, output.Path, cancellation.Token);

                Assert.Equal(firstSnapshot, CreateSceneSnapshot(secondManifest));
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
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

    private static async Task<GalleryManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Assert.IsType<GalleryManifest>(
            await JsonSerializer.DeserializeAsync<GalleryManifest>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken));
    }

    private static async Task AssertManifestMatchesPngsAsync(
        GalleryManifest manifest,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Assert.Equal("03", manifest.Task);
        Assert.Equal("NovelSpeaker.StyleGallery", manifest.Tool);
        Assert.Equal(GalleryRenderSettings.WindowWidth, manifest.WindowWidth);
        Assert.Equal(GalleryRenderSettings.WindowHeight, manifest.WindowHeight);
        Assert.Equal(GalleryRenderSettings.Dpi, manifest.Dpi);

        var registeredScenes = GallerySceneRegistry.All.ToDictionary(scene => scene.Name, StringComparer.Ordinal);
        Assert.Equal(registeredScenes.Count * 2, manifest.Scenes.Count);
        Assert.Equal(
            registeredScenes.Keys.Order(StringComparer.Ordinal),
            manifest.Scenes.Select(scene => scene.Scene).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Dark", "Light"],
            manifest.Scenes.Select(scene => scene.Theme).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        foreach (var entry in manifest.Scenes)
        {
            Assert.True(registeredScenes.TryGetValue(entry.Scene, out var scene));
            Assert.NotNull(scene);
            Assert.True(entry.Theme is "Light" or "Dark");
            Assert.Equal(scene!.Width, entry.Width);
            Assert.Equal(scene.Height, entry.Height);
            Assert.Equal(GalleryRenderSettings.Dpi, entry.Dpi);
            Assert.False(Path.IsPathRooted(entry.Png));
            Assert.Equal(entry.Png, Path.GetFileName(entry.Png));

            var pngPath = Path.Combine(outputDirectory, entry.Png);
            var pngBytes = await File.ReadAllBytesAsync(pngPath, cancellationToken);
            Assert.NotEmpty(pngBytes);
            Assert.Equal(
                entry.Sha256,
                Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant());

            await using var stream = new MemoryStream(pngBytes, writable: false);
            var frame = Assert.Single(
                BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames);
            Assert.Equal(entry.Width, frame.PixelWidth);
            Assert.Equal(entry.Height, frame.PixelHeight);
            Assert.InRange(
                frame.DpiX,
                GalleryRenderSettings.Dpi - 0.1,
                GalleryRenderSettings.Dpi + 0.1);
            Assert.InRange(
                frame.DpiY,
                GalleryRenderSettings.Dpi - 0.1,
                GalleryRenderSettings.Dpi + 0.1);
        }
    }

    private static string[] CreateSceneSnapshot(GalleryManifest manifest) =>
        manifest.Scenes
            .Select(scene => $"{scene.Theme}|{scene.Scene}|{scene.Width}x{scene.Height}|{scene.Dpi}")
            .Order(StringComparer.Ordinal)
            .ToArray();

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

        public string ManifestPath => System.IO.Path.Combine(Path, "manifest.json");

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
