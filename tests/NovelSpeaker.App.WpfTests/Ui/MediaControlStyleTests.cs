using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
        "PlaybackSliderThumbTemplate",
        "PlaybackSliderTrackButtonStyle",
        "PlaybackSliderThumbStyle",
        "PlaybackSliderControlTemplate",
        "App.Media.Button",
        "App.Media.Slider",
        "App.Media.ProgressSlider",
        "App.Media.VolumeSlider",
        "App.Media.ControlSurface"
    ];

    private static readonly string[] MediaButtonStyleKeys =
    [
        "App.Media.Button"
    ];

    private void Media_style_dictionary_contains_explicit_styles_without_templates()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Media.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = document.Root?.Elements().ToArray() ?? [];

        Assert.Equal(MediaStyleKeys, resources
            .Select(resource => (string?)resource.Attribute(xaml + "Key"))
            .ToArray());
        Assert.All(resources
            .Where(resource => ((string?)resource.Attribute(xaml + "Key"))?.StartsWith("App.Media.") == true)
            .Where(resource =>
                (string?)resource.Attribute(xaml + "Key") is not "App.Media.Slider" and not "App.Media.ProgressSlider"), resource =>
        {
            Assert.Equal("Style", resource.Name.LocalName);
            Assert.DoesNotContain(
                resource.Descendants(),
                element => element.Name.LocalName == "ControlTemplate" ||
                           (element.Name.LocalName == "Setter" &&
                            (string?)element.Attribute("Property") == "Template"));
        });
        var mediaButton = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.Button");
        Assert.Equal("{x:Type ui:Button}", (string?)mediaButton.Attribute("TargetType"));
        Assert.Equal("{StaticResource App.Button.Icon}", (string?)mediaButton.Attribute("BasedOn"));
        var mediaSlider = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.Slider");
        Assert.Equal("{x:Type Slider}", (string?)mediaSlider.Attribute("TargetType"));
        Assert.Equal("{StaticResource Provider.Slider}", (string?)mediaSlider.Attribute("BasedOn"));
        var mediaSliderTriggers = mediaSlider.Elements()
            .Single(element => element.Name.LocalName == "Style.Triggers")
            .Elements()
            .Select(trigger => (string?)trigger.Attribute("Property"))
            .ToArray();
        Assert.DoesNotContain("IsMouseOver", mediaSliderTriggers);
        var progressSlider = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.ProgressSlider");
        Assert.Equal("{x:Type Slider}", (string?)progressSlider.Attribute("TargetType"));
        Assert.Equal("{StaticResource App.Media.Slider}", (string?)progressSlider.Attribute("BasedOn"));
        var volumeSlider = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.VolumeSlider");
        Assert.Equal("{x:Type Slider}", (string?)volumeSlider.Attribute("TargetType"));
        Assert.Equal("{StaticResource App.Media.Slider}", (string?)volumeSlider.Attribute("BasedOn"));
        var controlSurface = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.ControlSurface");
        Assert.Equal("{x:Type Border}", (string?)controlSurface.Attribute("TargetType"));
        Assert.Equal("{StaticResource App.Surface.Secondary}", (string?)controlSurface.Attribute("BasedOn"));
    }

    private void Media_button_style_dictionary_contains_icon_based_style_without_template()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Media.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = document.Root?.Elements().ToArray() ?? [];

        Assert.Equal(
            MediaButtonStyleKeys,
            resources
                .Where(resource =>
                    (string?)resource.Attribute(xaml + "Key") == "App.Media.Button")
                .Select(resource => (string?)resource.Attribute(xaml + "Key"))
                .ToArray());
        var style = Assert.Single(resources, resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.Button");
        Assert.Equal("Style", style.Name.LocalName);
        Assert.Equal("{x:Type ui:Button}", (string?)style.Attribute("TargetType"));
        Assert.Equal("{StaticResource App.Button.Icon}", (string?)style.Attribute("BasedOn"));
        Assert.DoesNotContain(
            style.Descendants(),
            element => element.Name.LocalName == "ControlTemplate" ||
                       (element.Name.LocalName == "Setter" &&
                        (string?)element.Attribute("Property") == "Template"));
        var setterProperties = style.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .Select(element => element.Attribute("Property")?.Value ?? string.Empty)
            .ToArray();
        Assert.Equal(
            ["Width", "Height", "MinWidth", "MinHeight"],
            setterProperties.Take(4).ToArray());
    }

    private void Media_button_uses_content_feedback_without_surface_or_border_states()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Media.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = document.Root?.Elements()
            .Single(resource => (string?)resource.Attribute(xaml + "Key") == "App.Media.Button");

        Assert.NotNull(style);
        var setters = style!.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string?)element.Attribute("Value"));
        Assert.Equal("Transparent", setters["Background"]);
        Assert.Equal("{DynamicResource App.Brush.Text.Primary}", setters["Foreground"]);
        Assert.Equal("Transparent", setters["MouseOverBackground"]);
        Assert.Equal("Transparent", setters["PressedBackground"]);
        Assert.Equal("Transparent", setters["MouseOverBorderBrush"]);
        Assert.Equal("Transparent", setters["PressedBorderBrush"]);
        Assert.Equal(
            "{DynamicResource App.Brush.Interaction.Foreground.Pressed}",
            setters["PressedForeground"]);

        var triggers = style.Elements().Single(element => element.Name.LocalName == "Style.Triggers")
            .Elements()
            .Select(trigger => (string?)trigger.Attribute("Property") ?? string.Empty)
            .ToArray();
        Assert.Equal(["IsMouseOver", "IsPressed", "IsEnabled"], triggers);
        Assert.DoesNotContain(
            style.Elements().Single(element => element.Name.LocalName == "Style.Triggers").Elements(),
            trigger => trigger.Elements().Any(setter =>
                (string?)setter.Attribute("Property") == "BorderBrush" ||
                ((string?)setter.Attribute("Property") == "Background" &&
                 (string?)setter.Attribute("Value") != "Transparent")));
    }

    private void Media_slider_styles_share_track_thumb_template_and_select_geometry()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Media.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = document.Root!.Elements();
        var sharedResources = resources
            .Where(element =>
            {
                var key = (string?)element.Attribute(xaml + "Key");
                return key?.StartsWith("PlaybackSlider", StringComparison.Ordinal) == true;
            })
            .Select(element => (string?)element.Attribute(xaml + "Key") ?? string.Empty)
            .ToArray();

        Assert.Equal(
            [
                "PlaybackSliderThumbTemplate",
                "PlaybackSliderTrackButtonStyle",
                "PlaybackSliderThumbStyle",
                "PlaybackSliderControlTemplate"
            ],
            sharedResources);
        var controlTemplate = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "PlaybackSliderControlTemplate");
        Assert.Equal("{x:Type Slider}", (string?)controlTemplate.Attribute("TargetType"));
        var thumbStyle = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "PlaybackSliderThumbStyle");
        Assert.Equal(
            "{StaticResource PlaybackSliderThumbTemplate}",
            thumbStyle.Elements()
                .Single(element =>
                    element.Name.LocalName == "Setter" &&
                    (string?)element.Attribute("Property") == "Template")
                .Attribute("Value")?.Value);
        var thumbTemplate = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "PlaybackSliderThumbTemplate");
        Assert.Equal("{x:Type Thumb}", (string?)thumbTemplate.Attribute("TargetType"));
        var templateTriggers = controlTemplate.Descendants()
            .Where(element => element.Name.LocalName == "Trigger")
            .Select(element =>
                ((string?)element.Attribute("SourceName") ?? string.Empty,
                 (string?)element.Attribute("Property") ?? string.Empty))
            .ToArray();
        Assert.Contains((string.Empty, "Tag"), templateTriggers);
        Assert.Contains((string.Empty, "IsMouseOver"), templateTriggers);
        Assert.Contains((string.Empty, "IsKeyboardFocusWithin"), templateTriggers);
        Assert.Contains(("PART_MediaThumb", "IsDragging"), templateTriggers);
        var verticalTrigger = controlTemplate.Descendants()
            .Single(element => element.Name.LocalName == "Trigger" &&
                               (string?)element.Attribute("Property") == "Orientation" &&
                               (string?)element.Attribute("Value") == "Vertical");
        Assert.Equal(
            "4,-1",
            verticalTrigger.Descendants()
                .Single(setter => setter.Name.LocalName == "Setter" &&
                                  (string?)setter.Attribute("TargetName") == "PART_MediaDecreaseButton" &&
                                  (string?)setter.Attribute("Property") == "Margin")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "4,-1",
            verticalTrigger.Descendants()
                .Single(setter => setter.Name.LocalName == "Setter" &&
                                  (string?)setter.Attribute("TargetName") == "PART_MediaIncreaseButton" &&
                                  (string?)setter.Attribute("Property") == "Margin")
                .Attribute("Value")?.Value);

        var progress = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.ProgressSlider");
        var progressSetters = progress.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string?)element.Attribute("Value"));
        Assert.Equal("Progress", progressSetters["Tag"]);
        Assert.Equal("Transparent", progressSetters["Foreground"]);
        Assert.DoesNotContain(progressSetters.Keys, property => property == "Template");

        var volume = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.VolumeSlider");
        var volumeSetters = volume.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string?)element.Attribute("Value"));
        Assert.Equal("32", volumeSetters["Width"]);
        Assert.Equal("160", volumeSetters["Height"]);
        Assert.Equal("Vertical", volumeSetters["Orientation"]);
    }

    private void Media_slider_runtime_states_expose_shared_thumb_semantics()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var progress = new Slider
            {
                Style = Assert.IsType<Style>(application.FindResource("App.Media.ProgressSlider")),
                Margin = new Thickness(100),
                Minimum = 0,
                Maximum = 10,
                Value = 4
            };
            var volume = new Slider
            {
                Style = Assert.IsType<Style>(application.FindResource("App.Media.VolumeSlider")),
                Minimum = 0,
                Maximum = 1,
                Value = 0.4
            };
            var window = new Window
            {
                Content = new StackPanel
                {
                    Children = { progress, volume }
                },
                Width = 360,
                Height = 420,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();
                Assert.NotNull(progress.Template);
                Assert.Same(application.FindResource("PlaybackSliderControlTemplate"), progress.Template);
                Assert.Same(application.FindResource("PlaybackSliderControlTemplate"), volume.Template);
                Assert.Equal("Progress", progress.Tag);

                Assert.False(progress.IsMouseOver);
                Keyboard.ClearFocus();
                Assert.False(progress.IsKeyboardFocusWithin);
                var progressThumb = Assert.Single(FindDescendants<Thumb>(progress));
                var progressTrack = Assert.Single(FindDescendants<Track>(progress));
                Assert.Same(
                    application.FindResource("PlaybackSliderThumbStyle"),
                    progressThumb.Style);
                Assert.Equal(0, progressThumb.Opacity);
                Assert.True(progressTrack.ActualHeight >= 14);
                Assert.True(progressThumb.ActualWidth >= 12);
                Assert.True(progressThumb.ActualHeight >= 12);
                Assert.True(progress.Focus());
                window.UpdateLayout();
                Assert.Equal(1, progressThumb.Opacity);

                var volumeThumb = Assert.Single(FindDescendants<Thumb>(volume));
                var volumeTrack = Assert.Single(FindDescendants<Track>(volume));
                Assert.Equal(Orientation.Vertical, volume.Orientation);
                Assert.Same(
                    application.FindResource("PlaybackSliderThumbStyle"),
                    volumeThumb.Style);
                Assert.Equal(1, volumeThumb.Opacity);
                Assert.True(volumeTrack.ActualWidth >= 14);
                Assert.True(volumeThumb.ActualWidth >= 12);
                Assert.True(volumeThumb.ActualHeight >= 12);
                var accent = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Accent")).Color;
                var neutral = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Surface.Secondary")).Color;
                var trackBrushes = FindDescendants<Border>(volume)
                    .Select(border => (border.Background as SolidColorBrush)?.Color)
                    .Where(color => color.HasValue)
                    .Select(color => color!.Value)
                    .ToArray();
                Assert.Contains(accent, trackBrushes);
                Assert.Contains(neutral, trackBrushes);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    private void Gallery_media_fixture_distinguishes_navigation_icons_and_projects_slider_without_playback()
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
                    GetButtonIcon(bar.PreviousChapterButton).Symbol);
                Assert.Equal(
                    SymbolRegular.ChevronLeft20,
                    GetButtonIcon(bar.PreviousSegmentButton).Symbol);
                Assert.Equal(
                    SymbolRegular.ChevronRight20,
                    GetButtonIcon(bar.NextSegmentButton).Symbol);
                Assert.Equal(
                    SymbolRegular.ChevronDoubleRight20,
                    GetButtonIcon(bar.NextChapterButton).Symbol);
                Assert.Equal(
                    SymbolRegular.PlayCircle24,
                    GetButtonIcon(bar.PlayButton).Symbol);
                Assert.Equal(
                    SymbolRegular.PauseCircle24,
                    GetButtonIcon(bar.PauseButton).Symbol);

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

    private void Gallery_media_fixture_uses_equal_primary_button_and_icon_geometry()
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

                var playIcon = GetButtonIcon(bar.PlayButton);
                var pauseIcon = GetButtonIcon(bar.PauseButton);
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

    private void Gallery_media_fixture_binds_media_glyph_nodes_to_semantic_owner_foregrounds()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Gallery_media_fixture_binds_media_glyph_nodes_to_semantic_owner_foregrounds_for_theme(theme);
        }
    }

    private void Gallery_media_fixture_binds_media_glyph_nodes_to_semantic_owner_foregrounds_for_theme(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var bar = new GalleryMediaControlBar();
            var window = CreateFixtureWindow(bar, 900, 360);
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                    global::System.Windows.Application.Current);
                var expectedPrimary = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Text.Primary")).Color;

                AssertMediaGlyphForeground(bar.PlayButton, expectedPrimary);
                AssertMediaGlyphForeground(bar.PauseButton, expectedPrimary);
                AssertMediaGlyphForeground(bar.VolumeButton, expectedPrimary);
                AssertMediaGlyphForeground(bar.NextSegmentButton, expectedPrimary);

                var expectedDangerText = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Danger.Text")).Color;
                bar.CloseButton.Foreground = new SolidColorBrush(expectedDangerText);
                window.UpdateLayout();
                AssertMediaGlyphForeground(bar.CloseButton, expectedDangerText);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    private void Gallery_media_fixture_projects_accent_and_neutral_progress_tracks_during_drag()
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
                    application.FindResource("App.Brush.Accent")).Color;
                var expectedNeutral = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Surface.Secondary")).Color;
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

    private void Gallery_media_fixture_exposes_a_non_command_volume_button()
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
                    GetButtonIcon(bar.VolumeButton).Symbol);
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

    private void Gallery_media_fixture_is_measurable_in_both_themes()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Gallery_media_fixture_is_measurable_in_both_themes_for_theme(theme);
        }
    }

    private void Gallery_media_fixture_is_measurable_in_both_themes_for_theme(GalleryTheme theme)
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
                var mediaButtons = new[]
                {
                    bar.PreviousChapterButton,
                    bar.PreviousSegmentButton,
                    bar.PlayButton,
                    bar.PauseButton,
                    bar.NextSegmentButton,
                    bar.NextChapterButton,
                    bar.VolumeButton
                };
                Assert.All(mediaButtons, button =>
                {
                    Assert.Same(window.FindResource("App.Media.Button"), button.Style);
                    Assert.Equal(48, button.ActualWidth);
                    Assert.Equal(48, button.ActualHeight);
                });
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

    [Fact]
    public void Media_style_contracts_cover_explicit_ownership_and_inheritance()
    {
        Media_style_dictionary_contains_explicit_styles_without_templates();
        Media_button_style_dictionary_contains_icon_based_style_without_template();
        Media_button_uses_content_feedback_without_surface_or_border_states();
        Media_slider_styles_share_track_thumb_template_and_select_geometry();
        Media_slider_runtime_states_expose_shared_thumb_semantics();
    }

    [Fact]
    public void Media_fixture_contracts_cover_navigation_projection_geometry_and_controls()
    {
        Gallery_media_fixture_distinguishes_navigation_icons_and_projects_slider_without_playback();
        Gallery_media_fixture_uses_equal_primary_button_and_icon_geometry();
        Gallery_media_fixture_projects_accent_and_neutral_progress_tracks_during_drag();
        Gallery_media_fixture_exposes_a_non_command_volume_button();
    }

    [Fact]
    public void Media_fixture_theme_contracts_cover_foregrounds_and_measurable_layout()
    {
        Gallery_media_fixture_binds_media_glyph_nodes_to_semantic_owner_foregrounds();
        Gallery_media_fixture_is_measurable_in_both_themes();
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
        var icon = GetButtonIcon(button);
        var glyph = Assert.Single(FindDescendants<TextBlock>(icon));
        var foreground = Assert.IsType<SolidColorBrush>(glyph.Foreground);
        Assert.Equal(expectedColor, foreground.Color);
        Assert.NotEqual(Colors.Black, foreground.Color);
    }

    private static SymbolIcon GetButtonIcon(Button button) =>
        Assert.IsType<SymbolIcon>(Assert.IsType<Wpf.Ui.Controls.Button>(button).Icon);

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
