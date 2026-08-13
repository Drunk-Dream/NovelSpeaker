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
        var progressKeys = new[] { "App.Progress.Standard", "App.Progress.Compact" };
        var feedbackKeys = new[]
        {
            "App.Feedback.PopupSurface",
            "App.Feedback.FlyoutHost",
            "App.Feedback.DialogContent",
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
                "App.Progress.Standard" or "App.Progress.Compact" or "App.Media.Slider")
            .ToArray();
        Assert.All(progressStyles, AssertProviderStyleWithoutTemplate);
        Assert.Equal(
            "{x:Type ProgressBar}",
            progressStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Progress.Standard")
                .Attribute("TargetType")?.Value);
        Assert.Equal(
            "{x:Type ProgressBar}",
            progressStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Progress.Compact")
                .Attribute("TargetType")?.Value);
        Assert.Equal(
            "{x:Type Slider}",
            progressStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Media.Slider")
                .Attribute("TargetType")?.Value);

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
        Assert.Equal(
            "{StaticResource Provider.Snackbar}",
            feedbackStyles.Single(style => (string?)style.Attribute(xaml + "Key") == "App.Feedback.Snackbar")
                .Attribute("BasedOn")?.Value);

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
