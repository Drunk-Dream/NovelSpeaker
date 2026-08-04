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
            ["palette-probe", "placeholder-sections", "provider-controls", "provider-style-probe", "theme-resource-probe"],
            scenes.Select(scene => scene.Name).Order(StringComparer.Ordinal));
        Assert.All(scenes, scene =>
        {
            Assert.Equal(GalleryRenderSettings.WindowWidth, scene.Width);
            Assert.Equal(GalleryRenderSettings.WindowHeight, scene.Height);
        });
        Assert.Equal(96, GalleryRenderSettings.Dpi);
    }

    [Fact]
    public void Gallery_task_defaults_to_03_and_accepts_explicit_tasks_04_and_05()
    {
        var defaultOptions = GalleryCommandLineOptions.Parse(["--screenshot"]);
        var task04Options = GalleryCommandLineOptions.Parse(["--screenshot", "--task", "04"]);
        var task05Options = GalleryCommandLineOptions.Parse(["--screenshot", "--task", "05"]);

        Assert.Equal("03", defaultOptions.Task);
        Assert.Equal(Path.Combine("artifacts", "visual-review", "03"), defaultOptions.OutputDirectory);
        Assert.Equal("04", task04Options.Task);
        Assert.Equal("05", task05Options.Task);
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

            var bridgeProbe = GallerySceneRegistry.Build("provider-style-probe");
            bridgeProbe.Measure(new Size(GalleryRenderSettings.WindowWidth, GalleryRenderSettings.WindowHeight));
            bridgeProbe.Arrange(new Rect(0, 0, GalleryRenderSettings.WindowWidth, GalleryRenderSettings.WindowHeight));
            bridgeProbe.UpdateLayout();
            var bridgeControls = FindDescendants<Control>(bridgeProbe)
                .Where(control => AutomationProperties.GetName(control).StartsWith(
                    "Provider.",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(16, bridgeControls.Length);
            Assert.All(
                bridgeControls,
                control => Assert.NotNull(control.Template));

            var dynamicResourceElements = FindDescendants<FrameworkElement>(probeScene)
                .Where(element => element.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue ||
                                  element.ReadLocalValue(Control.ForegroundProperty) != DependencyProperty.UnsetValue)
                .ToArray();
            Assert.NotEmpty(dynamicResourceElements);
        });
    }

    [Fact]
    public void Palette_probe_updates_dynamic_brushes_without_replacing_style_or_template()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("palette-probe");
            var host = new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                host.Show();
                host.UpdateLayout();

                var swatches = FindDescendants<Border>(scene)
                    .Where(border => AutomationProperties.GetAutomationId(border).StartsWith(
                        "palette-",
                        StringComparison.Ordinal))
                    .ToDictionary(
                        border => AutomationProperties.GetAutomationId(border),
                        border => Assert.IsType<SolidColorBrush>(border.Background),
                        StringComparer.Ordinal);
                Assert.Equal(26, swatches.Count);

                var providerStyle = Assert.IsType<Style>(application.FindResource("Provider.Button"));
                var button = new WpfButton
                {
                    Content = "template stability fixture",
                    Style = providerStyle
                };
                button.Measure(new Size(240, 60));
                button.Arrange(new Rect(0, 0, 240, 60));
                button.ApplyTemplate();
                button.UpdateLayout();
                var template = button.Template;
                Assert.NotNull(template);

                var lightColors = swatches.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Color,
                    StringComparer.Ordinal);

                GalleryThemeRuntime.Apply(GalleryTheme.Dark);
                host.UpdateLayout();

                Assert.All(
                    swatches,
                    pair =>
                    {
                        var current = Assert.IsType<SolidColorBrush>(
                            FindDescendants<Border>(scene).Single(border =>
                                AutomationProperties.GetAutomationId(border) == pair.Key).Background);
                        Assert.NotEqual(lightColors[pair.Key], current.Color);
                    });
                Assert.Same(providerStyle, application.FindResource("Provider.Button"));
                Assert.Same(template, button.Template);

                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
            finally
            {
                host.Close();
            }
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
            GalleryWindow? firstWindow = null;
            GalleryWindow? secondWindow = null;
            try
            {
                var generator = new GalleryScreenshotGenerator();

                firstWindow = new GalleryWindow();
                firstWindow.Show();
                await generator.GenerateAsync(firstWindow, options, cancellation.Token);
                var firstManifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);
                await AssertManifestMatchesPngsAsync(firstManifest, output.Path, cancellation.Token);
                var firstSnapshot = CreateSceneSnapshot(firstManifest);
                firstWindow.Close();
                firstWindow = null;

                secondWindow = new GalleryWindow();
                secondWindow.Show();
                await generator.GenerateAsync(secondWindow, options, cancellation.Token);
                var secondManifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);
                await AssertManifestMatchesPngsAsync(secondManifest, output.Path, cancellation.Token);

                Assert.Equal(firstSnapshot, CreateSceneSnapshot(secondManifest));
            }
            finally
            {
                if (firstWindow?.IsVisible == true)
                {
                    firstWindow.Close();
                }

                if (secondWindow?.IsVisible == true)
                {
                    secondWindow.Close();
                }
            }
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_explicit_task_04_to_manifest()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--task",
                "04",
                "--theme",
                "all",
                "--scene",
                "provider-style-probe",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                window.Show();
                await new GalleryScreenshotGenerator().GenerateAsync(window, options, cancellation.Token);
                var manifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);

                await AssertManifestMatchesPngsAsync(
                    manifest,
                    output.Path,
                    "04",
                    ["provider-style-probe"],
                    cancellation.Token);
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

    [Fact]
    public async Task Screenshot_generator_writes_explicit_task_05_palette_scene_to_manifest()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--task",
                "05",
                "--theme",
                "all",
                "--scene",
                "palette-probe",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                window.Show();
                await new GalleryScreenshotGenerator().GenerateAsync(window, options, cancellation.Token);
                var manifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);

                await AssertManifestMatchesPngsAsync(
                    manifest,
                    output.Path,
                    "05",
                    ["palette-probe"],
                    cancellation.Token);
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
        await AssertManifestMatchesPngsAsync(
            manifest,
            outputDirectory,
            "03",
            GallerySceneRegistry.All.Select(scene => scene.Name).ToArray(),
            cancellationToken);
    }

    private static async Task AssertManifestMatchesPngsAsync(
        GalleryManifest manifest,
        string outputDirectory,
        string expectedTask,
        IReadOnlyCollection<string> expectedSceneNames,
        CancellationToken cancellationToken)
    {
        Assert.Equal(expectedTask, manifest.Task);
        Assert.Equal("NovelSpeaker.StyleGallery", manifest.Tool);
        Assert.Equal(GalleryRenderSettings.WindowWidth, manifest.WindowWidth);
        Assert.Equal(GalleryRenderSettings.WindowHeight, manifest.WindowHeight);
        Assert.Equal(GalleryRenderSettings.Dpi, manifest.Dpi);

        var registeredScenes = GallerySceneRegistry.All.ToDictionary(scene => scene.Name, StringComparer.Ordinal);
        Assert.Equal(expectedSceneNames.Count * 2, manifest.Scenes.Count);
        Assert.Equal(
            expectedSceneNames.Order(StringComparer.Ordinal),
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
