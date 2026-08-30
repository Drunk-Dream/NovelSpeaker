using System.IO;
using System.Xml.Linq;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class ProgressFeedbackStyleContractTests
{
    [Fact]
    public void Progress_and_feedback_resources_keep_unique_owners_and_provider_boundaries()
    {
        var stylesDirectory = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles");
        var progressKeys = new[] { "App.Progress.Standard", "App.Progress.Compact", "App.Progress.MediaTrack" };
        var feedbackKeys = new[]
        {
            "App.Feedback.PopupSurface",
            "App.Feedback.FlyoutHost",
            "App.Feedback.DialogBody",
            "App.Feedback.DialogTitle",
            "App.Feedback.DialogMessage",
            "App.Feedback.ValidationText",
            "App.Feedback.InlineMessage",
            "App.Feedback.SnackbarBody",
            "App.Feedback.SnackbarTitleTemplate",
            "App.Feedback.SnackbarMessageTemplate",
            "App.Feedback.Snackbar"
        };

        Assert.Equal(progressKeys, ReadKeys(Path.Combine(stylesDirectory, "Progress.xaml")));
        Assert.Equal(feedbackKeys, ReadKeys(Path.Combine(stylesDirectory, "Feedback.xaml")));

        var definitions = Directory
            .EnumerateFiles(stylesDirectory, "*.xaml", SearchOption.TopDirectoryOnly)
            .SelectMany(path => ReadKeys(path).Select(key => (path, key)))
            .ToArray();
        Assert.All(progressKeys.Concat(feedbackKeys), key =>
            Assert.Single(definitions, definition => definition.key == key));

        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var progress = XDocument.Load(Path.Combine(stylesDirectory, "Progress.xaml"));
        var media = XDocument.Load(Path.Combine(stylesDirectory, "Media.xaml"));
        var progressStyles = progress.Root!.Elements()
            .Concat(media.Root!.Elements())
            .Where(element => (string?)element.Attribute(xaml + "Key") is
                "App.Progress.Standard" or "App.Progress.Compact" or "App.Progress.MediaTrack" or
                "App.Media.Slider" or "App.Media.ProgressSlider" or "App.Media.VolumeSlider")
            .ToArray();
        Assert.All(
            progressStyles.Where(style =>
                (string?)style.Attribute(xaml + "Key") is not "App.Media.Slider" and not "App.Media.ProgressSlider"),
            AssertProviderStyleWithoutTemplate);
        Assert.Equal(
            "{x:Type ProgressBar}",
            progressStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Progress.Standard")
                .Attribute("TargetType")?.Value);
        Assert.Equal(
            "{x:Type ProgressBar}",
            progressStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Progress.Compact")
                .Attribute("TargetType")?.Value);
        var mediaTrack = progressStyles.Single(style =>
            (string?)style.Attribute(xaml + "Key") == "App.Progress.MediaTrack");
        Assert.Equal("{x:Type ProgressBar}", mediaTrack.Attribute("TargetType")?.Value);
        Assert.Equal("{StaticResource App.Progress.Standard}", mediaTrack.Attribute("BasedOn")?.Value);
        Assert.Contains(
            mediaTrack.Elements(),
            setter => setter.Name.LocalName == "Setter" &&
                      (string?)setter.Attribute("Property") == "Background" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Border.Subtle}");
        Assert.Equal(
            "{x:Type Slider}",
            progressStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Media.Slider")
                .Attribute("TargetType")?.Value);
        var progressSlider = progressStyles.Single(style =>
            (string?)style.Attribute(xaml + "Key") == "App.Media.ProgressSlider");
        Assert.Equal("{x:Type Slider}", progressSlider.Attribute("TargetType")?.Value);
        Assert.Equal("{StaticResource App.Media.Slider}", progressSlider.Attribute("BasedOn")?.Value);
        Assert.Equal(
            [
                "SliderTrackFill",
                "SliderTrackFillPointerOver"
            ],
            progressSlider.Elements().Single(element => element.Name.LocalName == "Style.Resources").Elements()
                .Select(resource => resource.Attribute(xaml + "Key")?.Value ?? string.Empty)
                .ToArray());
        var volumeSlider = progressStyles.Single(style =>
            (string?)style.Attribute(xaml + "Key") == "App.Media.VolumeSlider");
        Assert.Equal("{StaticResource App.Media.Slider}", volumeSlider.Attribute("BasedOn")?.Value);
        Assert.Contains(
            volumeSlider.Elements(),
            setter => setter.Name.LocalName == "Setter" &&
                      (string?)setter.Attribute("Property") == "Orientation" &&
                      (string?)setter.Attribute("Value") == "Vertical");

        var feedback = XDocument.Load(Path.Combine(stylesDirectory, "Feedback.xaml"));
        var feedbackResources = feedback.Root!.Elements().ToArray();
        Assert.Equal(11, feedbackResources.Length);
        var feedbackStyles = feedbackResources
            .Where(element => element.Name.LocalName == "Style")
            .ToArray();
        Assert.Equal(9, feedbackStyles.Length);
        Assert.All(feedbackStyles, AssertProviderStyleWithoutTemplate);
        Assert.DoesNotContain(
            feedbackStyles.SelectMany(style => style.Descendants()),
            element => element.Name.LocalName is "ContentControl" or "Popup" or "Window");
        Assert.Equal(
            "{StaticResource App.Surface.Popup}",
            feedbackStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Feedback.PopupSurface")
                .Attribute("BasedOn")?.Value);
        Assert.Equal(
            "{StaticResource Provider.Flyout}",
            feedbackStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Feedback.FlyoutHost")
                .Attribute("BasedOn")?.Value);
        var flyoutHostSetters = feedbackStyles.Single(style =>
                (string?)style.Attribute(xaml + "Key") == "App.Feedback.FlyoutHost")
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string?)element.Attribute("Value"));
        Assert.Equal("Transparent", flyoutHostSetters["Background"]);
        Assert.Equal("Transparent", flyoutHostSetters["BorderBrush"]);
        Assert.Equal("0", flyoutHostSetters["BorderThickness"]);
        Assert.Equal("0", flyoutHostSetters["Padding"]);
        Assert.Equal("{x:Null}", flyoutHostSetters["Effect"]);
        Assert.Equal(
            "{StaticResource Provider.Snackbar}",
            feedbackStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Feedback.Snackbar")
                .Attribute("BasedOn")?.Value);
        var dialogBody = feedbackStyles.Single(style =>
            (string?)style.Attribute(xaml + "Key") == "App.Feedback.DialogBody");
        Assert.Null(dialogBody.Attribute("BasedOn"));
        var dialogBodySetters = dialogBody.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string?)element.Attribute("Value"));
        Assert.Equal("0", dialogBodySetters["BorderThickness"]);
        Assert.Equal("0", dialogBodySetters["Padding"]);
        Assert.Equal("Transparent", dialogBodySetters["Background"]);
        Assert.Equal("{x:Null}", dialogBodySetters["Effect"]);

        var templates = feedbackResources
            .Where(resource => resource.Name.LocalName == "DataTemplate")
            .ToDictionary(
                resource => (string)resource.Attribute(xaml + "Key")!,
                resource => resource.Descendants().Single(element => element.Name.LocalName == "TextBlock"));
        Assert.Equal(
            "{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type ui:Snackbar}}}",
            templates["App.Feedback.SnackbarTitleTemplate"].Attribute("Foreground")?.Value);
        Assert.Equal(
            "{Binding ContentForeground, RelativeSource={RelativeSource AncestorType={x:Type ui:Snackbar}}}",
            templates["App.Feedback.SnackbarMessageTemplate"].Attribute("Foreground")?.Value);

        AssertVolumeFlyoutPercentageLayout(Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "Playback",
            "Components",
            "PlayerView.xaml"));
        AssertVolumeFlyoutPercentageLayout(Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Desktop",
            "MiniPlayer",
            "MiniPlayerWindow.xaml"));
    }

    private static void AssertVolumeFlyoutPercentageLayout(string path)
    {
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var flyout = XDocument.Load(path).Descendants()
            .Single(element => element.Name.LocalName == "Flyout" &&
                               (string?)element.Attribute(xaml + "Name") is "VolumeFlyout" or "MiniPlayerVolumeFlyout");
        var textBlocks = flyout.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .ToArray();

        var surface = Assert.Single(flyout.Elements(), element => element.Name.LocalName == "Border");
        Assert.Equal("96", (string?)surface.Attribute("Width"));
        Assert.Equal("{StaticResource App.Feedback.PopupSurface}", (string?)surface.Attribute("Style"));
        Assert.Equal(
            "-24",
            flyout.Attributes()
                .Single(attribute => attribute.Name.LocalName.EndsWith(".HorizontalOffset", StringComparison.Ordinal))
                .Value);
        Assert.DoesNotContain(textBlocks, textBlock => (string?)textBlock.Attribute("Text") == "播放音量");
        var percentage = Assert.Single(textBlocks, textBlock =>
            (string?)textBlock.Attribute("Text") == "{Binding VolumePercentText}");
        Assert.Equal("Center", (string?)percentage.Attribute("HorizontalAlignment"));
    }

    private static void AssertProviderStyleWithoutTemplate(XElement style)
    {
        Assert.Equal("Style", style.Name.LocalName);
        Assert.DoesNotContain(
            style.Descendants(),
            element => element.Name.LocalName == "ControlTemplate" ||
                       (element.Name.LocalName == "Setter" &&
                        (string?)element.Attribute("Property") == "Template"));
    }

    private static string[] ReadKeys(string path)
    {
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        return (XDocument.Load(path).Root?.Elements() ?? [])
            .Select(resource => resource.Attribute(xaml + "Key")?.Value ?? string.Empty)
            .ToArray();
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
