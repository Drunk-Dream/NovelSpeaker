using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Desktop.MiniPlayer;
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
        "App.Media.PlaybackSliderThumbTemplate",
        "App.Media.PlaybackSliderTrackButtonStyle",
        "App.Media.VisualTrackButton",
        "App.Media.VisualTrack",
        "App.Media.PlaybackSliderThumbStyle",
        "App.Media.PlaybackSliderControlTemplate",
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
                (string?)resource.Attribute(xaml + "Key") is not "App.Media.Slider" and
                not "App.Media.ProgressSlider" and
                not "App.Media.VisualTrackButton" and
                not "App.Media.VisualTrack" and
                not "App.Media.PlaybackSliderThumbTemplate" and
                not "App.Media.PlaybackSliderTrackButtonStyle" and
                not "App.Media.PlaybackSliderThumbStyle" and
                not "App.Media.PlaybackSliderControlTemplate"), resource =>
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
                return key?.StartsWith("App.Media.PlaybackSlider", StringComparison.Ordinal) == true;
            })
            .Select(element => (string?)element.Attribute(xaml + "Key") ?? string.Empty)
            .ToArray();

        Assert.Equal(
            [
                "App.Media.PlaybackSliderThumbTemplate",
                "App.Media.PlaybackSliderTrackButtonStyle",
                "App.Media.PlaybackSliderThumbStyle",
                "App.Media.PlaybackSliderControlTemplate"
            ],
            sharedResources);
        var controlTemplate = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.PlaybackSliderControlTemplate");
        Assert.Equal("{x:Type Slider}", (string?)controlTemplate.Attribute("TargetType"));
        var thumbStyle = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.PlaybackSliderThumbStyle");
        Assert.Equal(
            "{StaticResource App.Media.PlaybackSliderThumbTemplate}",
            thumbStyle.Elements()
                .Single(element =>
                    element.Name.LocalName == "Setter" &&
                    (string?)element.Attribute("Property") == "Template")
                .Attribute("Value")?.Value);
        var thumbTemplate = resources.Single(resource =>
            (string?)resource.Attribute(xaml + "Key") == "App.Media.PlaybackSliderThumbTemplate");
        Assert.Equal("{x:Type Thumb}", (string?)thumbTemplate.Attribute("TargetType"));
        var thumbSurface = thumbTemplate.Descendants()
            .Single(element => element.Name.LocalName == "Border" &&
                               (string?)element.Attribute(xaml + "Name") == "PART_ThumbSurface");
        Assert.Equal("14", (string?)thumbSurface.Attribute("Width"));
        Assert.Equal("14", (string?)thumbSurface.Attribute("Height"));
        var thumbTemplateTriggers = thumbTemplate.Descendants()
            .Where(element => element.Name.LocalName == "Trigger")
            .Select(element => ((string?)element.Attribute("Property") ?? string.Empty,
                                (string?)element.Attribute("Value") ?? string.Empty))
            .ToArray();
        Assert.Contains(("IsDragging", "True"), thumbTemplateTriggers);
        var thumbStyleSetters = thumbStyle.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string?)element.Attribute("Value"));
        Assert.Equal("16", thumbStyleSetters["Width"]);
        Assert.Equal("16", thumbStyleSetters["Height"]);
        var draggingStyleTrigger = thumbStyle.Elements()
            .Single(element => element.Name.LocalName == "Style.Triggers")
            .Elements()
            .Single(element => (string?)element.Attribute("Property") == "IsDragging");
        Assert.DoesNotContain(
            draggingStyleTrigger.Elements(),
            setter => (string?)setter.Attribute("Property") is "Width" or "Height");
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
        var decreaseWidth = verticalTrigger.Descendants()
            .Single(setter => setter.Name.LocalName == "Setter" &&
                              (string?)setter.Attribute("TargetName") == "PART_MediaDecreaseButton" &&
                              (string?)setter.Attribute("Property") == "Width")
            .Attribute("Value")?.Value;
        var increaseWidth = verticalTrigger.Descendants()
            .Single(setter => setter.Name.LocalName == "Setter" &&
                              (string?)setter.Attribute("TargetName") == "PART_MediaIncreaseButton" &&
                              (string?)setter.Attribute("Property") == "Width")
            .Attribute("Value")?.Value;
        Assert.False(string.IsNullOrWhiteSpace(decreaseWidth));
        Assert.Equal(decreaseWidth, increaseWidth);
        Assert.True(double.TryParse(
            decreaseWidth,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var fixedWidth));
        Assert.True(fixedWidth > 0);
        Assert.DoesNotContain(
            verticalTrigger.Descendants(),
            setter => setter.Name.LocalName == "Setter" &&
                      (string?)setter.Attribute("Property") == "Margin" &&
                      (string?)setter.Attribute("Value") == "4,-1");

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
                Assert.Same(application.FindResource("App.Media.PlaybackSliderControlTemplate"), progress.Template);
                Assert.Same(application.FindResource("App.Media.PlaybackSliderControlTemplate"), volume.Template);
                Assert.Equal("Progress", progress.Tag);
                var progressVisualTrack = Assert.Single(FindDescendants<ProgressBar>(progress));
                Assert.Equal(Visibility.Collapsed, progressVisualTrack.Visibility);

                Assert.False(progress.IsMouseOver);
                Keyboard.ClearFocus();
                Assert.False(progress.IsKeyboardFocusWithin);
                var progressThumb = Assert.Single(FindDescendants<Thumb>(progress));
                var progressTrack = Assert.Single(FindDescendants<Track>(progress));
                Assert.Same(
                    application.FindResource("App.Media.PlaybackSliderThumbStyle"),
                    progressThumb.Style);
                Assert.Equal(0, progressThumb.Opacity);
                Assert.True(progressTrack.ActualHeight >= 14);
                Assert.True(progressThumb.ActualWidth >= 12);
                Assert.True(progressThumb.ActualHeight >= 12);
                Assert.True(progress.Focus());
                window.UpdateLayout();
                Assert.Equal(1, progressThumb.Opacity);

                var volumeThumb = Assert.Single(FindDescendants<Thumb>(volume));
                var volumeTrack = Assert.Single(
                    FindDescendants<Track>(volume),
                    track => track.Name == "PART_Track");
                var volumeTrackButtons = FindDescendants<RepeatButton>(volume)
                    .Where(button => button.Style == application.FindResource("App.Media.PlaybackSliderTrackButtonStyle"))
                    .ToArray();
                var visualTrack = Assert.Single(FindDescendants<ProgressBar>(volume));
                var visualRail = Assert.Single(FindDescendants<Track>(visualTrack));
                var visualTrackButtons = FindDescendants<RepeatButton>(visualTrack)
                    .Where(button => button.Style == application.FindResource("App.Media.VisualTrackButton"))
                    .ToArray();
                Assert.Equal(Orientation.Vertical, volume.Orientation);
                Assert.Same(
                    application.FindResource("App.Media.PlaybackSliderThumbStyle"),
                    volumeThumb.Style);
                Assert.Equal(1, volumeThumb.Opacity);
                Assert.Equal(16, volumeTrack.ActualWidth, 3);
                Assert.Equal(16, volumeThumb.ActualWidth, 3);
                Assert.Equal(16, volumeThumb.ActualHeight, 3);
                Assert.Equal(2, volumeTrackButtons.Length);
                Assert.Equal(2, visualTrackButtons.Length);
                Assert.Equal(Visibility.Visible, visualTrack.Visibility);
                Assert.All(volumeTrackButtons, button =>
                    Assert.Equal(HorizontalAlignment.Center, button.HorizontalAlignment));
                Assert.Equal(volumeTrackButtons[0].ActualWidth, volumeTrackButtons[1].ActualWidth, 3);
                Assert.All(volumeTrackButtons, button => Assert.True(button.ActualWidth > 0));
                Assert.All(visualTrackButtons, button =>
                {
                    Assert.False(button.IsHitTestVisible);
                    Assert.Equal(visualRail.ActualWidth, button.ActualWidth, 3);
                    Assert.InRange(button.ActualWidth, 5.5, 6.5);
                });

                foreach (var value in new[] { 0d, 0.5d, 1d })
                {
                    volume.Value = value;
                    window.UpdateLayout();
                    var bounds = volumeTrackButtons
                        .Select(button => new Rect(
                            button.TranslatePoint(new Point(), volumeTrack),
                            button.RenderSize))
                        .OrderBy(rect => rect.Top)
                        .ToArray();
                    var thumbBounds = new Rect(
                        volumeThumb.TranslatePoint(new Point(), volumeTrack),
                        volumeThumb.RenderSize);
                    Assert.Equal(2, bounds.Length);
                    Assert.Equal(bounds[0].Width, bounds[1].Width, 3);
                    Assert.All(bounds, rect =>
                    {
                        Assert.InRange(rect.Left, -1, volumeTrack.ActualWidth + 1);
                        Assert.InRange(rect.Right, -1, volumeTrack.ActualWidth + 1);
                        Assert.InRange(rect.Top, -1, volumeTrack.ActualHeight + 1);
                        Assert.InRange(rect.Bottom, -1, volumeTrack.ActualHeight + 1);
                    });
                    Assert.InRange(thumbBounds.Left, -1, volumeTrack.ActualWidth + 1);
                    Assert.InRange(thumbBounds.Right, -1, volumeTrack.ActualWidth + 1);
                    Assert.InRange(thumbBounds.Top, -1, volumeTrack.ActualHeight + 1);
                    Assert.InRange(thumbBounds.Bottom, -1, volumeTrack.ActualHeight + 1);
                    if (value is 0d)
                    {
                        Assert.True(bounds[0].Height > 1);
                        Assert.InRange(bounds[0].Top, -1, 1);
                        Assert.InRange(Math.Abs(bounds[0].Bottom - thumbBounds.Top), 0, 1);
                        Assert.InRange(bounds[1].Height, 0, 1);
                        Assert.InRange(Math.Abs(thumbBounds.Bottom - volumeTrack.ActualHeight), 0, 1);
                    }
                    else if (value is 1d)
                    {
                        Assert.InRange(bounds[0].Height, 0, 1);
                        Assert.InRange(Math.Abs(thumbBounds.Top), 0, 1);
                        Assert.True(bounds[1].Height > 1);
                        Assert.InRange(Math.Abs(thumbBounds.Bottom - bounds[1].Top), 0, 1);
                        Assert.InRange(volumeTrack.ActualHeight - bounds[1].Bottom, -1, 1);
                    }
                    else
                    {
                        Assert.True(bounds[0].Height > 1);
                        Assert.True(bounds[1].Height > 1);
                        Assert.InRange(bounds[0].Top, -1, 1);
                        Assert.InRange(Math.Abs(bounds[0].Bottom - thumbBounds.Top), 0, 1);
                        Assert.InRange(Math.Abs(thumbBounds.Bottom - bounds[1].Top), 0, 1);
                        Assert.InRange(volumeTrack.ActualHeight - bounds[1].Bottom, -1, 1);
                    }
                }
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
                Assert.DoesNotContain(
                    FindDescendants<Border>(visualTrack),
                    border => border.CornerRadius != new CornerRadius());
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    private void Media_volume_slider_rendering_keeps_a_continuous_rail_at_the_thumb()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            WpfTestHost.RunInSta(() =>
            {
                GalleryThemeRuntime.EnsureProviderResources();
                GalleryThemeRuntime.Apply(theme);
                var volume = new Slider
                {
                    Style = Assert.IsType<Style>(
                        global::System.Windows.Application.Current!.FindResource("App.Media.VolumeSlider")),
                    Minimum = 0,
                    Maximum = 1,
                    Value = 0.5
                };
                var root = new Grid
                {
                    Width = 64,
                    Height = 192,
                    Background = Brushes.White
                };
                volume.VerticalAlignment = VerticalAlignment.Center;
                root.Children.Add(volume);
                using var host = new WpfControlHost(root);
                try
                {
                    var rootSize = new Size(64, 192);
                    host.MeasureArrange(rootSize);
                    var thumb = Assert.Single(FindDescendants<Thumb>(volume));
                    var visualTrack = Assert.Single(FindDescendants<ProgressBar>(volume));
                    var visualRail = Assert.Single(FindDescendants<Track>(visualTrack));
                    var thumbBounds = new Rect(
                        thumb.TranslatePoint(new Point(), root),
                        thumb.RenderSize);
                    var bitmap = Render(root, rootSize);
                    var above = Math.Max(0, (int)Math.Ceiling(thumbBounds.Top) - 1);
                    var below = Math.Min(bitmap.PixelHeight - 1, (int)Math.Floor(thumbBounds.Bottom));
                    var accent = Assert.IsType<SolidColorBrush>(
                        global::System.Windows.Application.Current!.FindResource("App.Brush.Accent")).Color;
                    var neutral = Assert.IsType<SolidColorBrush>(
                        global::System.Windows.Application.Current.FindResource("App.Brush.Surface.Secondary")).Color;
                    var aboveWidth = CountRailPixels(bitmap, above, accent, neutral);
                    var belowWidth = CountRailPixels(bitmap, below, accent, neutral);
                    var railProfile = Enumerable.Range(0, bitmap.PixelHeight)
                        .Select(y => (Y: y, Width: CountRailPixels(bitmap, y, accent, neutral)))
                        .ToArray();
                    var maxRailWidth = railProfile.MaxBy(sample => sample.Width);
                    var restSliderSize = volume.RenderSize;
                    var restTrackBounds = new Rect(
                        visualRail.TranslatePoint(new Point(), root),
                        visualRail.RenderSize);
                    var restAboveWidth = aboveWidth;
                    var restBelowWidth = belowWidth;

                    Assert.InRange(visualRail.ActualWidth, 5.5, 6.5);
                    Assert.True(
                        aboveWidth is >= 5 and <= 7,
                        $"thumb={thumbBounds}, volume={volume.RenderSize}, visualTrack={visualTrack.RenderSize}, " +
                        $"visualRail={visualRail.RenderSize}, above={above}, below={below}, " +
                        $"aboveWidth={aboveWidth}, belowWidth={belowWidth}, maxRailWidth={maxRailWidth}");
                    Assert.Equal(aboveWidth, belowWidth);
                    AssertRailPixelsAreContiguous(bitmap, above, accent, neutral);
                    AssertRailPixelsAreContiguous(bitmap, below, accent, neutral);

                    SetThumbDragging(thumb, true);
                    root.UpdateLayout();
                    var draggingThumbBounds = new Rect(
                        thumb.TranslatePoint(new Point(), root),
                        thumb.RenderSize);
                    var draggingTrackBounds = new Rect(
                        visualRail.TranslatePoint(new Point(), root),
                        visualRail.RenderSize);
                    var draggingBitmap = Render(root, rootSize);
                    var draggingAbove = Math.Max(0, (int)Math.Ceiling(draggingThumbBounds.Top) - 1);
                    var draggingBelow = Math.Min(
                        draggingBitmap.PixelHeight - 1,
                        (int)Math.Floor(draggingThumbBounds.Bottom));
                    var draggingAboveWidth = CountRailPixels(
                        draggingBitmap,
                        draggingAbove,
                        accent,
                        neutral);
                    var draggingBelowWidth = CountRailPixels(
                        draggingBitmap,
                        draggingBelow,
                        accent,
                        neutral);

                    Assert.Equal(restSliderSize, volume.RenderSize);
                    Assert.Equal(restTrackBounds.X, draggingTrackBounds.X, 3);
                    Assert.Equal(restTrackBounds.Y, draggingTrackBounds.Y, 3);
                    Assert.Equal(restTrackBounds.Width, draggingTrackBounds.Width, 3);
                    Assert.Equal(restTrackBounds.Height, draggingTrackBounds.Height, 3);
                    Assert.Equal(restAboveWidth, draggingAboveWidth);
                    Assert.Equal(restBelowWidth, draggingBelowWidth);
                    Assert.Equal(draggingAboveWidth, draggingBelowWidth);
                    AssertRailPixelsAreContiguous(draggingBitmap, draggingAbove, accent, neutral);
                    AssertRailPixelsAreContiguous(draggingBitmap, draggingBelow, accent, neutral);
                }
                finally
                {
                    GalleryThemeRuntime.Apply(GalleryTheme.Light);
                }
            });
        }
    }

    private void Media_volume_slider_flyout_hosts_keep_the_thumb_complete_in_player_and_mini_player()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                WpfTestHost.RunInSta(() =>
                {
                    GalleryThemeRuntime.EnsureProviderResources();
                    GalleryThemeRuntime.Apply(theme);
                    var dpi = 96 * scale;
                    var dpiScale = new DpiScale(scale, scale);
                    WpfWindowHost? playerHost = null;
                    Window? playerWindow = null;
                    Wpf.Ui.Controls.Flyout? playerFlyout = null;
                    ServiceProvider? provider = null;
                    MiniPlayerWindow? miniWindow = null;
                    WpfWindowHost? miniHost = null;
                    try
                    {
                        var playerView = new PlayerView();
                        VisualTreeHelper.SetRootDpi(playerView, dpiScale);
                        playerWindow = new Window
                        {
                            Content = playerView,
                            Width = 1280,
                            Height = 760,
                            ShowInTaskbar = false,
                            WindowStyle = WindowStyle.ToolWindow
                        };
                        playerHost = WpfWindowHost.Show(playerWindow);
                        playerWindow.UpdateLayout();
                        playerFlyout = Assert.IsType<Wpf.Ui.Controls.Flyout>(
                            playerView.FindName("VolumeFlyout"));
                        playerFlyout.IsOpen = true;
                        playerWindow.Dispatcher.Invoke(DispatcherPriority.Render, static () => { });
                        AssertVolumeFlyoutStates(
                            playerView,
                            playerFlyout,
                            Assert.IsType<Slider>(playerView.FindName("VolumeSlider")),
                            dpi);
                        playerFlyout.IsOpen = false;

                        provider = WpfTestHost.BuildServiceProvider();
                        miniWindow = provider.GetRequiredService<MiniPlayerWindow>();
                        VisualTreeHelper.SetRootDpi(miniWindow, dpiScale);
                        miniHost = WpfWindowHost.Show(miniWindow);
                        miniWindow.UpdateLayout();
                        var miniFlyout = Assert.IsType<Wpf.Ui.Controls.Flyout>(
                            miniWindow.FindName("MiniPlayerVolumeFlyout"));
                        miniFlyout.IsOpen = true;
                        miniWindow.Dispatcher.Invoke(DispatcherPriority.Render, static () => { });
                        AssertVolumeFlyoutStates(
                            miniWindow,
                            miniFlyout,
                            Assert.IsType<Slider>(miniWindow.FindName("MiniPlayerVolumeSlider")),
                            dpi);
                        miniFlyout.IsOpen = false;
                    }
                    finally
                    {
                        try
                        {
                            if (playerFlyout is not null)
                            {
                                playerFlyout.IsOpen = false;
                            }

                            if (miniWindow?.FindName("MiniPlayerVolumeFlyout") is Wpf.Ui.Controls.Flyout miniFlyout)
                            {
                                miniFlyout.IsOpen = false;
                            }
                        }
                        finally
                        {
                            try
                            {
                                miniWindow?.CloseForShutdown();
                            }
                            finally
                            {
                                try
                                {
                                    miniHost?.Dispose();
                                }
                                finally
                                {
                                    try
                                    {
                                        playerHost?.Dispose();
                                    }
                                    finally
                                    {
                                        try
                                        {
                                            provider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                                        }
                                        finally
                                        {
                                            GalleryThemeRuntime.Apply(GalleryTheme.Light);
                                        }
                                    }
                                }
                            }
                        }
                    }
                });
            }
        }
    }

    private static void AssertVolumeFlyoutStates(
        FrameworkElement owner,
        Wpf.Ui.Controls.Flyout flyout,
        Slider slider,
        double dpi)
    {
        flyout.ApplyTemplate();
        var popup = Assert.IsType<Popup>(flyout.Template.FindName("PART_Popup", flyout));
        var popupRoot = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
        var scale = dpi / 96;
        VisualTreeHelper.SetRootDpi(FindVisualRoot(popupRoot), new DpiScale(scale, scale));
        var actualDpi = VisualTreeHelper.GetDpi(popupRoot);
        Assert.Equal(scale, actualDpi.DpiScaleX, 3);
        Assert.Equal(scale, actualDpi.DpiScaleY, 3);
        popupRoot.UpdateLayout();
        slider.UpdateLayout();
        var thumb = Assert.Single(FindDescendants<Thumb>(slider));
        var track = Assert.Single(
            FindDescendants<Track>(slider),
            candidate => candidate.Name == "PART_Track");
        var thumbSurface = Assert.Single(
            FindDescendants<Border>(thumb),
            border => border.Name == "PART_ThumbSurface");
        var restSliderBounds = GetBounds(slider, popupRoot);
        var restTrackBounds = GetBounds(track, popupRoot);
        var restThumbBounds = GetBounds(thumb, popupRoot);
        var accentDefault = Assert.IsType<SolidColorBrush>(
            global::System.Windows.Application.Current!.FindResource("App.Brush.Accent.Default")).Color;
        var restSurfaceSpan = CaptureSurfaceSpan(
            owner,
            restThumbBounds,
            dpi,
            accentDefault);
        var localThumbBounds = new Rect(new Point(), thumb.RenderSize);
        var restThumbBitmap = RenderVisual(thumb, thumb.RenderSize, dpi);
        var restThumbAlphaSpan = FindSurfaceSpan(
            restThumbBitmap,
            localThumbBounds,
            scale,
            accentDefault);
        var restThumbCoreSpan = FindExactSurfaceSpan(
            restThumbBitmap,
            localThumbBounds,
            scale,
            accentDefault);

        (int Min, int Max) hoverSurfaceSpan;
        (int Min, int Max) hoverThumbAlphaSpan;
        (int Min, int Max) hoverThumbCoreSpan;
        try
        {
            SetReadOnlyProperty(slider, typeof(UIElement), "IsMouseOverPropertyKey", true);
            popupRoot.UpdateLayout();
            var hoverColor = Assert.IsType<SolidColorBrush>(
                global::System.Windows.Application.Current.FindResource("App.Brush.Accent.Hover")).Color;
            hoverSurfaceSpan = CaptureSurfaceSpan(
                owner,
                restThumbBounds,
                dpi,
                hoverColor);
            var hoverThumbBitmap = RenderVisual(thumb, thumb.RenderSize, dpi);
            hoverThumbAlphaSpan = FindSurfaceSpan(
                hoverThumbBitmap,
                localThumbBounds,
                scale,
                hoverColor);
            hoverThumbCoreSpan = FindExactSurfaceSpan(
                hoverThumbBitmap,
                localThumbBounds,
                scale,
                hoverColor);
        }
        finally
        {
            SetReadOnlyProperty(slider, typeof(UIElement), "IsMouseOverPropertyKey", false);
        }

        Assert.Equal(32, slider.Width, 3);
        Assert.Equal(160, slider.Height, 3);
        Assert.Equal(16, track.ActualWidth, 3);
        Assert.Equal(16, thumb.ActualWidth, 3);
        Assert.InRange(thumbSurface.ActualWidth, 13, 15);
        Assert.InRange(thumbSurface.ActualHeight, 13, 15);
        AssertSurfaceGeometry(restThumbAlphaSpan, localThumbBounds, dpi, 14);
        AssertSurfaceGeometry(hoverThumbAlphaSpan, localThumbBounds, dpi, 14);
        AssertHostSurfacePreservesCore(restSurfaceSpan, restThumbCoreSpan, restThumbBounds, dpi);
        AssertHostSurfacePreservesCore(hoverSurfaceSpan, hoverThumbCoreSpan, restThumbBounds, dpi);

        try
        {
            SetReadOnlyProperty(slider, typeof(UIElement), "IsMouseOverPropertyKey", true);
            SetThumbDragging(thumb, true);
            popupRoot.UpdateLayout();
            var draggingSliderBounds = GetBounds(slider, popupRoot);
            var draggingTrackBounds = GetBounds(track, popupRoot);
            var draggingThumbBounds = GetBounds(thumb, popupRoot);
            var draggingSurfaceSpan = CaptureSurfaceSpan(
                owner,
                draggingThumbBounds,
                dpi,
                Assert.IsType<SolidColorBrush>(
                    global::System.Windows.Application.Current.FindResource("App.Brush.Accent.Pressed")).Color,
                Assert.IsType<SolidColorBrush>(
                global::System.Windows.Application.Current.FindResource("App.Brush.Focus")).Color);
            var draggingThumbBitmap = RenderVisual(thumb, thumb.RenderSize, dpi);
            var draggingThumbAlphaSpan = FindSurfaceSpan(
                draggingThumbBitmap,
                localThumbBounds,
                scale,
                Assert.IsType<SolidColorBrush>(
                    global::System.Windows.Application.Current.FindResource("App.Brush.Accent.Pressed")).Color,
                Assert.IsType<SolidColorBrush>(
                    global::System.Windows.Application.Current.FindResource("App.Brush.Focus")).Color);
            var draggingThumbCoreSpan = FindExactSurfaceSpan(
                draggingThumbBitmap,
                localThumbBounds,
                scale,
                Assert.IsType<SolidColorBrush>(
                    global::System.Windows.Application.Current.FindResource("App.Brush.Accent.Pressed")).Color,
                Assert.IsType<SolidColorBrush>(
                    global::System.Windows.Application.Current.FindResource("App.Brush.Focus")).Color);
            Assert.Equal(restSliderBounds.X, draggingSliderBounds.X, 3);
            Assert.Equal(restSliderBounds.Y, draggingSliderBounds.Y, 3);
            Assert.Equal(restSliderBounds.Width, draggingSliderBounds.Width, 3);
            Assert.Equal(restSliderBounds.Height, draggingSliderBounds.Height, 3);
            Assert.Equal(restTrackBounds.X, draggingTrackBounds.X, 3);
            Assert.Equal(restTrackBounds.Y, draggingTrackBounds.Y, 3);
            Assert.Equal(restTrackBounds.Width, draggingTrackBounds.Width, 3);
            Assert.Equal(restTrackBounds.Height, draggingTrackBounds.Height, 3);
            Assert.Equal(restThumbBounds.X, draggingThumbBounds.X, 3);
            Assert.Equal(restThumbBounds.Y, draggingThumbBounds.Y, 3);
            Assert.Equal(restThumbBounds.Width, draggingThumbBounds.Width, 3);
            Assert.Equal(restThumbBounds.Height, draggingThumbBounds.Height, 3);
            Assert.InRange(thumbSurface.ActualWidth, 15, 17);
            Assert.InRange(thumbSurface.ActualHeight, 15, 17);
            AssertSurfaceGeometry(draggingThumbAlphaSpan, localThumbBounds, dpi, 16);
            AssertHostSurfacePreservesCore(
                draggingSurfaceSpan,
                draggingThumbCoreSpan,
                draggingThumbBounds,
                dpi);
        }
        finally
        {
            SetThumbDragging(thumb, false);
            SetReadOnlyProperty(slider, typeof(UIElement), "IsMouseOverPropertyKey", false);
        }
    }

    private static (int Min, int Max) CaptureSurfaceSpan(
        FrameworkElement owner,
        Rect thumbBounds,
        double dpi,
        params Color[] surfaceColors)
    {
        var layer = Assert.Single(TransientPopupVisualRenderer.CaptureOpenHostLayers(owner, dpi));
        var scale = dpi / 96;
        var y = Math.Clamp(
            (int)Math.Round((thumbBounds.Top + thumbBounds.Height / 2) * scale),
            0,
            layer.Bitmap.PixelHeight - 1);
        var left = Math.Max(0, (int)Math.Floor(thumbBounds.Left * scale) - 2);
        var right = Math.Min(
            layer.Bitmap.PixelWidth - 1,
            (int)Math.Ceiling(thumbBounds.Right * scale) + 2);
        var centerX = Math.Clamp(
            (int)Math.Round((thumbBounds.Left + thumbBounds.Width / 2) * scale),
            0,
            layer.Bitmap.PixelWidth - 1);
        Assert.Contains(ReadPixel(layer.Bitmap, centerX, y), surfaceColors);
        var colored = Enumerable.Range(left, right - left + 1)
            .Where(x => surfaceColors.Contains(ReadPixel(layer.Bitmap, x, y)))
            .ToArray();
        Assert.NotEmpty(colored);
        return (colored.Min(), colored.Max());
    }

    private static (int Min, int Max) FindExactSurfaceSpan(
        BitmapSource bitmap,
        Rect bounds,
        double scale,
        params Color[] surfaceColors)
    {
        var y = Math.Clamp(
            (int)Math.Round((bounds.Top + bounds.Height / 2) * scale),
            0,
            bitmap.PixelHeight - 1);
        var left = Math.Max(0, (int)Math.Floor(bounds.Left * scale) - 2);
        var right = Math.Min(
            bitmap.PixelWidth - 1,
            (int)Math.Ceiling(bounds.Right * scale) + 2);
        var centerX = Math.Clamp(
            (int)Math.Round((bounds.Left + bounds.Width / 2) * scale),
            0,
            bitmap.PixelWidth - 1);
        Assert.Contains(ReadPixel(bitmap, centerX, y), surfaceColors);
        var colored = Enumerable.Range(left, right - left + 1)
            .Where(x => surfaceColors.Contains(ReadPixel(bitmap, x, y)))
            .ToArray();
        Assert.NotEmpty(colored);
        return (colored.Min(), colored.Max());
    }

    private static void AssertHostSurfacePreservesCore(
        (int Min, int Max) hostSpan,
        (int Min, int Max) localSpan,
        Rect hostBounds,
        double dpi)
    {
        var scale = dpi / 96;
        var hostCenter = (hostBounds.Left + hostBounds.Width / 2) * scale;
        var localCenter = hostBounds.Width / 2 * scale;
        var hostLeftRadius = hostCenter - hostSpan.Min;
        var hostRightRadius = hostSpan.Max - hostCenter;
        var localLeftRadius = localCenter - localSpan.Min;
        var localRightRadius = localSpan.Max - localCenter;
        Assert.InRange(Math.Abs(hostLeftRadius - localLeftRadius), 0, 1);
        Assert.InRange(Math.Abs(hostRightRadius - localRightRadius), 0, 1);
        Assert.InRange(
            hostSpan.Max - hostSpan.Min,
            localSpan.Max - localSpan.Min - 1,
            localSpan.Max - localSpan.Min + 1);
    }

    private static void AssertSurfaceGeometry(
        (int Min, int Max) span,
        Rect bounds,
        double dpi,
        double expectedDip)
    {
        var scale = dpi / 96;
        var expectedCenter = (bounds.Left + bounds.Width / 2) * scale;
        var leftRadius = expectedCenter - span.Min;
        var rightRadius = span.Max - expectedCenter;
        var expectedRadius = expectedDip * scale / 2;
        Assert.InRange(Math.Abs(leftRadius - rightRadius), 0, 1);
        Assert.InRange(leftRadius, expectedRadius - 2, expectedRadius + 2);
        Assert.InRange(rightRadius, expectedRadius - 2, expectedRadius + 2);
        Assert.InRange(
            (double)(span.Max - span.Min + 1),
            expectedDip * scale - 2,
            expectedDip * scale + 2);
    }

    private static Rect GetBounds(FrameworkElement element, FrameworkElement ancestor) =>
        new(element.TranslatePoint(new Point(), ancestor), element.RenderSize);

    private static Visual FindVisualRoot(Visual visual)
    {
        while (VisualTreeHelper.GetParent(visual) is Visual parent)
        {
            visual = parent;
        }

        return visual;
    }

    private void Media_volume_slider_dragging_keeps_a_fixed_centered_thumb_envelope()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                WpfTestHost.RunInSta(() =>
                {
                    GalleryThemeRuntime.EnsureProviderResources();
                    GalleryThemeRuntime.Apply(theme);
                    var volume = new Slider
                    {
                        Style = Assert.IsType<Style>(
                            global::System.Windows.Application.Current!.FindResource("App.Media.VolumeSlider")),
                        Minimum = 0,
                        Maximum = 1,
                        Value = 0.5,
                        SnapsToDevicePixels = true
                    };
                    var root = new Grid
                    {
                        Width = 64,
                        Height = 192,
                        Background = Brushes.Transparent,
                        SnapsToDevicePixels = true
                    };
                    root.Children.Add(volume);
                    VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
                    using var host = new WpfControlHost(root);
                    try
                    {
                        var rootSize = new Size(64, 192);
                        host.MeasureArrange(rootSize);
                        var thumb = Assert.Single(FindDescendants<Thumb>(volume));
                        var track = Assert.Single(
                            FindDescendants<Track>(volume),
                            candidate => candidate.Name == "PART_Track");
                        var thumbSurface = Assert.Single(
                            FindDescendants<Border>(thumb),
                            border => border.Name == "PART_ThumbSurface");
                        var restBounds = new Rect(
                            thumb.TranslatePoint(new Point(), root),
                            thumb.RenderSize);
                        Assert.Equal(16, thumb.ActualWidth, 3);
                        Assert.Equal(16, thumb.ActualHeight, 3);
                        Assert.Equal(16, track.ActualWidth, 3);
                        Assert.Equal(14, thumbSurface.ActualWidth, 3);
                        Assert.Equal(14, thumbSurface.ActualHeight, 3);

                        var restBitmap = Render(root, rootSize, 96 * scale);
                        var accentDefault = Assert.IsType<SolidColorBrush>(
                            global::System.Windows.Application.Current.FindResource("App.Brush.Accent.Default")).Color;
                        var restSpan = FindSurfaceSpan(
                            restBitmap,
                            restBounds,
                            scale,
                            accentDefault);
                        (int Min, int Max) hoverSpan;
                        try
                        {
                            SetReadOnlyProperty(volume, typeof(UIElement), "IsMouseOverPropertyKey", true);
                            root.UpdateLayout();
                            var hoverColor = Assert.IsType<SolidColorBrush>(
                                global::System.Windows.Application.Current.FindResource("App.Brush.Accent.Hover")).Color;
                            hoverSpan = FindSurfaceSpan(
                                Render(root, rootSize, 96 * scale),
                                restBounds,
                                scale,
                                hoverColor);
                        }
                        finally
                        {
                            SetReadOnlyProperty(volume, typeof(UIElement), "IsMouseOverPropertyKey", false);
                        }

                        try
                        {
                            SetThumbDragging(thumb, true);
                            root.UpdateLayout();
                            var draggingBounds = new Rect(
                                thumb.TranslatePoint(new Point(), root),
                                thumb.RenderSize);
                            Assert.Equal(restBounds.X, draggingBounds.X, 3);
                            Assert.Equal(restBounds.Y, draggingBounds.Y, 3);
                            Assert.Equal(restBounds.Width, draggingBounds.Width, 3);
                            Assert.Equal(restBounds.Height, draggingBounds.Height, 3);
                            Assert.Equal(16, thumbSurface.ActualWidth, 3);
                            Assert.Equal(16, thumbSurface.ActualHeight, 3);

                            var draggingBitmap = Render(root, rootSize, 96 * scale);
                            var pressed = Assert.IsType<SolidColorBrush>(
                                global::System.Windows.Application.Current.FindResource("App.Brush.Accent.Pressed")).Color;
                            var focus = Assert.IsType<SolidColorBrush>(
                                global::System.Windows.Application.Current.FindResource("App.Brush.Focus")).Color;
                            var draggingSpan = FindSurfaceSpan(
                                draggingBitmap,
                                draggingBounds,
                                scale,
                                pressed,
                                focus);
                            var expectedCenter = (restBounds.Left + (restBounds.Width / 2)) * scale;

                            Assert.InRange(
                                Math.Abs(((restSpan.Min + restSpan.Max) / 2d) - expectedCenter),
                                0,
                                1);
                            Assert.InRange(
                                Math.Abs(((draggingSpan.Min + draggingSpan.Max) / 2d) - expectedCenter),
                                0,
                                1);
                            AssertSurfaceGeometry(restSpan, restBounds, 96 * scale, 14);
                            AssertSurfaceGeometry(hoverSpan, restBounds, 96 * scale, 14);
                            AssertSurfaceGeometry(draggingSpan, draggingBounds, 96 * scale, 16);
                            Assert.InRange(
                                restSpan.Max - restSpan.Min + 1,
                                Math.Round(14 * scale) - 1d,
                                Math.Round(14 * scale) + 1d);
                            Assert.InRange(
                                draggingSpan.Max - draggingSpan.Min + 1,
                                Math.Round(16 * scale) - 1d,
                                Math.Round(16 * scale) + 1d);
                        }
                        finally
                        {
                            SetThumbDragging(thumb, false);
                            SetReadOnlyProperty(volume, typeof(UIElement), "IsMouseOverPropertyKey", false);
                        }
                    }
                    finally
                    {
                        GalleryThemeRuntime.Apply(GalleryTheme.Light);
                    }
                });
            }
        }
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
        Media_volume_slider_rendering_keeps_a_continuous_rail_at_the_thumb();
        Media_volume_slider_dragging_keeps_a_fixed_centered_thumb_envelope();
        Media_volume_slider_flyout_hosts_keep_the_thumb_complete_in_player_and_mini_player();
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

    private static BitmapSource Render(FrameworkElement root, Size size, double dpi = 96)
    {
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Round(size.Width * dpi / 96)),
            Math.Max(1, (int)Math.Round(size.Height * dpi / 96)),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource RenderVisual(Visual visual, Size size, double dpi)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(
                new VisualBrush(visual)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                },
                null,
                new Rect(new Point(), size));
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Round(size.Width * dpi / 96)),
            Math.Max(1, (int)Math.Round(size.Height * dpi / 96)),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    private static (int Min, int Max) FindSurfaceSpan(
        BitmapSource bitmap,
        Rect bounds,
        double scale,
        params Color[] surfaceColors)
    {
        var y = Math.Clamp(
            (int)Math.Round((bounds.Top + bounds.Height / 2) * scale),
            0,
            bitmap.PixelHeight - 1);
        var left = Math.Max(0, (int)Math.Floor(bounds.Left * scale) - 2);
        var right = Math.Min(
            bitmap.PixelWidth - 1,
            (int)Math.Ceiling(bounds.Right * scale) + 2);
        var centerX = Math.Clamp(
            (int)Math.Round((bounds.Left + bounds.Width / 2) * scale),
            0,
            bitmap.PixelWidth - 1);
        var centerPixel = ReadPixel(bitmap, centerX, y);
        Assert.Contains(centerPixel, surfaceColors);
        var colored = Enumerable.Range(left, right - left + 1)
            .Where(x => ReadPixel(bitmap, x, y).A > 0)
            .ToArray();
        Assert.NotEmpty(colored);
        return (colored.Min(), colored.Max());
    }

    private static Color ReadPixel(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    private static void SetThumbDragging(Thumb thumb, bool value)
    {
        SetReadOnlyProperty(thumb, typeof(Thumb), "IsDraggingPropertyKey", value);
    }

    private static void SetReadOnlyProperty(
        DependencyObject target,
        Type declaringType,
        string keyFieldName,
        bool value)
    {
        var key = Assert.IsType<DependencyPropertyKey>(
            declaringType.GetField(keyFieldName, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null));
        var setValue = typeof(DependencyObject).GetMethod(
            nameof(DependencyObject.SetValue),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(DependencyPropertyKey), typeof(object)],
            modifiers: null);
        Assert.NotNull(setValue);
        setValue!.Invoke(target, [key, value]);
    }

    private static int CountRailPixels(BitmapSource bitmap, int y, Color accent, Color neutral)
    {
        var pixels = new byte[bitmap.PixelWidth * 4];
        bitmap.CopyPixels(new Int32Rect(0, y, bitmap.PixelWidth, 1), pixels, bitmap.PixelWidth * 4, 0);
        var count = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var color = Color.FromArgb(
                pixels[offset + 3],
                pixels[offset + 2],
                pixels[offset + 1],
                pixels[offset]);
            if (color == accent || color == neutral)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertRailPixelsAreContiguous(
        BitmapSource bitmap,
        int y,
        Color accent,
        Color neutral)
    {
        var pixels = new byte[bitmap.PixelWidth * 4];
        bitmap.CopyPixels(new Int32Rect(0, y, bitmap.PixelWidth, 1), pixels, bitmap.PixelWidth * 4, 0);
        var first = -1;
        var last = -1;
        var count = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var color = Color.FromArgb(
                pixels[offset + 3],
                pixels[offset + 2],
                pixels[offset + 1],
                pixels[offset]);
            if (color != accent && color != neutral)
            {
                continue;
            }

            var x = offset / 4;
            first = first < 0 ? x : first;
            last = x;
            count++;
        }

        Assert.True(count > 0);
        Assert.Equal(count, last - first + 1);
    }

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
