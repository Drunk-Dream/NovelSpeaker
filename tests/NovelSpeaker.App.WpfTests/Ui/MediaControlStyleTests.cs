using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using NovelSpeaker.StyleGallery;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class MediaControlStyleTests
{
    private static readonly string[] MediaStyleKeys =
    [
        "App.Media.Primary",
        "App.Media.Secondary",
        "App.Media.Chapter",
        "App.Media.WindowAction",
        "App.Media.Slider"
    ];

    [Fact]
    public void Gallery_command_line_accepts_explicit_task_08_media_scene()
    {
        var options = GalleryCommandLineOptions.Parse(
        [
            "--screenshot",
            "--task",
            "08",
            "--theme",
            "all",
            "--scene",
            "media-controls"
        ]);

        Assert.Equal("08", options.Task);
        Assert.Equal("media-controls", options.SceneName);
        Assert.Equal(GalleryThemeChoice.All, options.Theme);
    }

    [Fact]
    public async Task Screenshot_generator_writes_explicit_task_08_media_scene_manifest()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--task",
                "08",
                "--theme",
                "all",
                "--scene",
                "media-controls",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                window.Show();
                var manifest = await new GalleryScreenshotGenerator().GenerateAsync(
                    window,
                    options,
                    cancellation.Token);

                Assert.Equal("08", manifest.Task);
                Assert.Equal(
                    ["Dark", "Light"],
                    manifest.Scenes.Select(scene => scene.Theme).Order(StringComparer.Ordinal));
                Assert.All(manifest.Scenes, scene =>
                {
                    Assert.Equal("media-controls", scene.Scene);
                    Assert.Equal(GalleryRenderSettings.WindowWidth, scene.Width);
                    Assert.Equal(GalleryRenderSettings.WindowHeight, scene.Height);
                    var pngPath = Path.Combine(output.Path, scene.Png);
                    var pngBytes = File.ReadAllBytes(pngPath);
                    Assert.Equal(
                        scene.Sha256,
                        Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant());
                    using var stream = new MemoryStream(pngBytes, writable: false);
                    var frame = Assert.Single(
                        BitmapDecoder.Create(
                            stream,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad).Frames);
                    Assert.Equal(scene.Width, frame.PixelWidth);
                    Assert.Equal(scene.Height, frame.PixelHeight);
                });
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
    public void Media_style_dictionary_contains_explicit_provider_based_styles_without_templates()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "MediaControlStyles.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = document.Root?.Elements().ToArray() ?? [];

        Assert.Equal(
            MediaStyleKeys,
            resources.Select(resource => (string?)resource.Attribute(xaml + "Key")).ToArray());
        Assert.All(resources, resource =>
        {
            Assert.Equal("Style", resource.Name.LocalName);
            Assert.Equal(
                resource.Attribute("TargetType")?.Value == "Slider" ? "{StaticResource Provider.Slider}" : "{StaticResource Provider.Button}",
                (string?)resource.Attribute("BasedOn"));
            Assert.DoesNotContain(
                resource.Descendants(),
                element => element.Name.LocalName == "ControlTemplate" ||
                           (element.Name.LocalName == "Setter" &&
                            (string?)element.Attribute("Property") == "Template"));
        });
    }

    [Fact]
    public void Media_controls_meet_minimum_geometry_and_visual_weight_contract()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            var primary = CreateButton(application, "App.Media.Primary", SymbolRegular.PlayCircle24);
            var secondary = CreateButton(application, "App.Media.Secondary", SymbolRegular.ChevronLeft20);
            var chapter = CreateButton(application, "App.Media.Chapter", SymbolRegular.ChevronDoubleLeft20);
            var windowAction = CreateButton(application, "App.Media.WindowAction", SymbolRegular.Pin24);
            var slider = new Slider
            {
                Style = Assert.IsType<Style>(application.FindResource("App.Media.Slider")),
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                Width = 320
            };

            foreach (var control in new Control[] { primary, secondary, chapter, windowAction, slider })
            {
                stack.Children.Add(control);
            }

            var window = new Window
            {
                Content = stack,
                Width = 760,
                Height = 160,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal(48, primary.ActualWidth);
                Assert.Equal(48, primary.ActualHeight);
                Assert.Equal(36, secondary.ActualWidth);
                Assert.Equal(36, secondary.ActualHeight);
                Assert.Equal(32, chapter.ActualWidth);
                Assert.Equal(32, chapter.ActualHeight);
                Assert.Equal(32, windowAction.ActualWidth);
                Assert.Equal(32, windowAction.ActualHeight);
                Assert.True(slider.ActualWidth >= 280);
                Assert.True(slider.ActualHeight >= 24);

                var primaryBackground = Assert.IsType<SolidColorBrush>(primary.Background);
                var secondaryBackground = Assert.IsType<SolidColorBrush>(secondary.Background);
                Assert.NotEqual(primaryBackground.Color, secondaryBackground.Color);
                Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(chapter.Background).Color);
                Assert.True(primary.Foreground is SolidColorBrush);
                Assert.True(chapter.Foreground is SolidColorBrush);
                Assert.NotNull(primary.Template);
                Assert.NotNull(slider.Template);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Gallery_media_fixture_distinguishes_navigation_icons_and_projects_slider_without_playback()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var bar = new GalleryMediaControlBar();
            var window = new Window
            {
                Content = bar,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal(
                    SymbolRegular.ChevronDoubleLeft20,
                    Assert.IsType<SymbolIcon>(bar.PreviousChapterButton.Content).Symbol);
                Assert.Equal(
                    SymbolRegular.ChevronLeft20,
                    Assert.IsType<SymbolIcon>(bar.PreviousSegmentButton.Content).Symbol);
                Assert.Equal(
                    SymbolRegular.ChevronRight20,
                    Assert.IsType<SymbolIcon>(bar.NextSegmentButton.Content).Symbol);
                Assert.Equal(
                    SymbolRegular.ChevronDoubleRight20,
                    Assert.IsType<SymbolIcon>(bar.NextChapterButton.Content).Symbol);
                Assert.Equal(
                    SymbolRegular.PlayCircle24,
                    Assert.IsType<SymbolIcon>(bar.PlayButton.Content).Symbol);
                Assert.Equal(
                    SymbolRegular.PauseCircle24,
                    Assert.IsType<SymbolIcon>(bar.PauseButton.Content).Symbol);

                Assert.True(bar.PinButton.IsEnabled);
                Assert.False(bar.DisabledWindowActionButton.IsEnabled);
                Assert.Contains("置顶", Assert.IsType<string>(bar.PinButton.ToolTip), StringComparison.Ordinal);
                Assert.Contains("Disabled", Assert.IsType<string>(bar.DisabledWindowActionButton.ToolTip), StringComparison.Ordinal);
                Assert.True(bar.PauseButton.Focus());
                Assert.True(bar.PauseButton.IsKeyboardFocused);

                var toolTip = Assert.IsType<ToolTip>(bar.ProgressSlider.ToolTip);
                Assert.Same(bar.ProgressSlider, toolTip.PlacementTarget);
                Assert.Equal("58 / 140", toolTip.Content);
                Assert.True(toolTip.StaysOpen);
                Assert.True(bar.SliderProjection.IsDragging);

                var playbackClicks = 0;
                bar.PlayButton.Click += (_, _) => playbackClicks++;
                var initialSize = bar.ProgressSlider.RenderSize;
                bar.ProgressSlider.Value = 91;
                window.UpdateLayout();

                Assert.Equal(91, bar.SliderProjection.Value);
                Assert.Equal("91 / 140", toolTip.Content);
                Assert.Contains("91 / 140", bar.SliderProjectionText.Text, StringComparison.Ordinal);
                Assert.Equal(0, playbackClicks);
                Assert.Equal(initialSize, bar.ProgressSlider.RenderSize);
            }
            finally
            {
                bar.SliderProjection.EndDrag();
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Gallery_media_fixture_is_measurable_in_both_themes(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var bar = new GalleryMediaControlBar();
            var window = new Window
            {
                Content = bar,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.True(bar.ActualWidth > 0);
                Assert.True(bar.ActualHeight > 0);
                Assert.True(bar.ProgressSlider.ActualWidth >= 280);
                Assert.True(bar.PlayButton.ActualWidth >= 48);
                Assert.True(bar.PreviousSegmentButton.ActualWidth >= 36);
                Assert.True(bar.PreviousChapterButton.ActualWidth >= 32);
                Assert.DoesNotContain(
                    new[] { bar.ActualWidth, bar.ActualHeight, bar.ProgressSlider.ActualWidth },
                    double.IsNaN);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    private static Button CreateButton(
        global::System.Windows.Application application,
        string styleKey,
        SymbolRegular symbol) =>
        new()
        {
            Style = Assert.IsType<Style>(application.FindResource(styleKey)),
            Content = new SymbolIcon { Symbol = symbol }
        };

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
