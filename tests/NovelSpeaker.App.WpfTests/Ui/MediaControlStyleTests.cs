using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using NovelSpeaker.StyleGallery;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class MediaControlStyleTests
{
    private static readonly string[] MediaStyleKeys =
    [
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
                WpfWindowHost.Show(window);
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
    public void Media_slider_style_dictionary_contains_explicit_provider_based_style_without_template()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "SliderStyles.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = document.Root?.Elements().ToArray() ?? [];

        Assert.Equal(
            MediaStyleKeys,
            resources.Select(resource => (string?)resource.Attribute(xaml + "Key")).ToArray());
        Assert.All(resources, resource =>
        {
            Assert.Equal("Style", resource.Name.LocalName);
            Assert.Equal("{StaticResource Provider.Slider}", (string?)resource.Attribute("BasedOn"));
            Assert.DoesNotContain(
                resource.Descendants(),
                element => element.Name.LocalName == "ControlTemplate" ||
                           (element.Name.LocalName == "Setter" &&
                            (string?)element.Attribute("Property") == "Template"));
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
                WpfWindowHost.Show(window);
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

    [Fact]
    public void Gallery_media_fixture_uses_equal_primary_button_and_icon_geometry()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var bar = new GalleryMediaControlBar();
            var window = CreateFixtureWindow(bar, 900, 360);
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                Assert.Equal(bar.PlayButton.RenderSize, bar.PauseButton.RenderSize);
                Assert.True(bar.PlayButton.ActualWidth >= 32);
                Assert.True(bar.PlayButton.ActualHeight >= 32);

                var playIcon = Assert.IsType<SymbolIcon>(bar.PlayButton.Content);
                var pauseIcon = Assert.IsType<SymbolIcon>(bar.PauseButton.Content);
                Assert.True(playIcon.RenderSize.Width > 0);
                Assert.True(playIcon.RenderSize.Height > 0);
                Assert.Equal(playIcon.RenderSize, pauseIcon.RenderSize);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    [Fact]
    public void Gallery_media_fixture_binds_dark_media_glyph_nodes_to_semantic_foregrounds()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Dark);
            var bar = new GalleryMediaControlBar();
            var window = CreateFixtureWindow(bar, 900, 360);
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                    global::System.Windows.Application.Current);
                var expectedPrimary = Assert.IsType<SolidColorBrush>(
                    application.FindResource("PrimaryTextBrush")).Color;

                AssertMediaGlyphForeground(bar.PlayButton, expectedPrimary);
                AssertMediaGlyphForeground(bar.PauseButton, expectedPrimary);
                AssertMediaGlyphForeground(bar.VolumeButton, expectedPrimary);
                AssertMediaGlyphForeground(bar.NextSegmentButton, expectedPrimary);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    [Fact]
    public void Gallery_media_fixture_projects_accent_and_neutral_progress_tracks_during_drag()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var bar = new GalleryMediaControlBar();
            var window = CreateFixtureWindow(bar, 900, 360);
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                    global::System.Windows.Application.Current);
                var expectedAccent = Assert.IsType<SolidColorBrush>(
                    application.FindResource("AccentBrush")).Color;
                var expectedNeutral = Assert.IsType<SolidColorBrush>(
                    application.FindResource("SecondarySurfaceBrush")).Color;
                var played = FindDescendants<Border>(bar).Single(border =>
                    AutomationProperties.GetAutomationId(border) == "media-slider-played-track");
                var unplayed = FindDescendants<Border>(bar).Single(border =>
                    AutomationProperties.GetAutomationId(border) == "media-slider-unplayed-track");

                Assert.Equal(expectedAccent, Assert.IsType<SolidColorBrush>(played.Background).Color);
                Assert.Equal(expectedNeutral, Assert.IsType<SolidColorBrush>(unplayed.Background).Color);
                Assert.True(played.ActualWidth > 0);
                Assert.True(unplayed.ActualWidth > 0);
                var initialPlayedWidth = played.ActualWidth;

                bar.ProgressSlider.Value = 112;
                window.UpdateLayout();

                Assert.Equal(112, bar.SliderProjection.Value);
                Assert.True(played.ActualWidth > initialPlayedWidth);
                Assert.True(unplayed.ActualWidth < bar.ProgressTrack.ActualWidth);
                Assert.Equal("112 / 140", Assert.IsType<ToolTip>(bar.ProgressSlider.ToolTip).Content);
            }
            finally
            {
                bar.SliderProjection.EndDrag();
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    [Fact]
    public void Gallery_media_fixture_exposes_a_non_command_volume_button()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var bar = new GalleryMediaControlBar();
            var window = CreateFixtureWindow(bar, 900, 360);
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                Assert.Equal(
                    SymbolRegular.Speaker224,
                    Assert.IsType<SymbolIcon>(bar.VolumeButton.Content).Symbol);
                Assert.Contains("音量", AutomationProperties.GetName(bar.VolumeButton), StringComparison.Ordinal);
                Assert.Contains("音量", Assert.IsType<string>(bar.VolumeButton.ToolTip), StringComparison.Ordinal);
                Assert.True(bar.VolumeButton.ActualWidth >= 36);
                Assert.True(bar.VolumeButton.ActualHeight >= 36);

                var clickCount = 0;
                bar.VolumeButton.Click += (_, _) => clickCount++;
                bar.ProgressSlider.Value = 96;
                window.UpdateLayout();
                Assert.Equal(0, clickCount);
            }
            finally
            {
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
                WpfWindowHost.Show(window);
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

    private static Window CreateFixtureWindow(FrameworkElement content, double width, double height) =>
        new()
        {
            Content = content,
            Width = width,
            Height = height,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow
        };

    private static void AssertMediaGlyphForeground(Button button, Color expectedColor)
    {
        var icon = Assert.IsType<SymbolIcon>(button.Content);
        var glyph = Assert.Single(FindDescendants<TextBlock>(icon));
        var foreground = Assert.IsType<SolidColorBrush>(glyph.Foreground);
        Assert.Equal(expectedColor, foreground.Color);
        Assert.NotEqual(Colors.Black, foreground.Color);
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
