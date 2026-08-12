using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using NovelSpeaker.StyleGallery;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class ProgressFeedbackStyleContractTests
{
    [Fact]
    public void Progress_and_feedback_keys_are_owned_by_their_style_dictionaries()
    {
        var stylesDirectory = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles");

        Assert.Equal(
            ["App.Progress.Standard", "App.Progress.Compact"],
            ReadKeys(Path.Combine(stylesDirectory, "Progress.xaml")));
        Assert.Equal(
            [
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
            ],
            ReadKeys(Path.Combine(stylesDirectory, "Feedback.xaml")));

        var definitions = Directory
            .EnumerateFiles(stylesDirectory, "*.xaml", SearchOption.TopDirectoryOnly)
            .SelectMany(path => ReadKeys(path).Select(key => (path, key)))
            .ToArray();
        Assert.All(
            new[]
            {
                "App.Progress.Standard",
                "App.Progress.Compact",
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
            },
            key => Assert.Single(definitions, definition => definition.key == key));
    }

    [Fact]
    public void Progress_styles_preserve_progressbar_and_slider_type_boundaries_without_templates()
    {
        var root = LocateRepositoryRoot();
        var progress = XDocument.Load(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Progress.xaml"));
        var media = XDocument.Load(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Media.xaml"));
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var styles = progress.Root!.Elements().Concat(media.Root!.Elements())
            .Where(element => (string?)element.Attribute(xaml + "Key") is
                "App.Progress.Standard" or "App.Progress.Compact" or "App.Media.Slider")
            .ToArray();

        Assert.All(styles, style =>
        {
            Assert.Equal("Style", style.Name.LocalName);
            Assert.DoesNotContain(
                style.Descendants(),
                element => element.Name.LocalName == "ControlTemplate" ||
                           (element.Name.LocalName == "Setter" &&
                            (string?)element.Attribute("Property") == "Template"));
        });
        Assert.Equal(
            "{x:Type ProgressBar}",
            (string?)styles.Single(style =>
                (string?)style.Attribute(xaml + "Key") == "App.Progress.Standard")
                .Attribute("TargetType"));
        Assert.Equal(
            "{x:Type ProgressBar}",
            (string?)styles.Single(style =>
                (string?)style.Attribute(xaml + "Key") == "App.Progress.Compact")
                .Attribute("TargetType"));
        Assert.Equal(
            "{x:Type Slider}",
            (string?)styles.Single(style =>
                (string?)style.Attribute(xaml + "Key") == "App.Media.Slider")
                .Attribute("TargetType"));
    }

    [Fact]
    public void Feedback_resources_style_provider_hosts_without_replacing_their_templates()
    {
        var document = XDocument.Load(Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Feedback.xaml"));
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var styles = document.Root?.Elements().ToArray() ?? [];

        Assert.Equal(11, styles.Length);
        var styleElements = styles.Where(style => style.Name.LocalName == "Style").ToArray();
        Assert.Equal(9, styleElements.Length);
        Assert.All(styleElements, style =>
        {
            Assert.DoesNotContain(
                style.Descendants(),
                element => element.Name.LocalName == "ControlTemplate" ||
                           (string?)element.Attribute("Property") == "Template");
        });
        Assert.DoesNotContain(
            styleElements.SelectMany(style => style.Descendants()),
            element => element.Name.LocalName is "ContentControl" or "Popup" or "Window");
        Assert.Equal(
            "{StaticResource App.Surface.Popup}",
            (string?)styleElements[0].Attribute("BasedOn"));
        Assert.Equal(
            "{StaticResource Provider.Flyout}",
            (string?)styleElements[1].Attribute("BasedOn"));
        Assert.Equal(
            "{StaticResource Provider.Snackbar}",
            (string?)styleElements[^1].Attribute("BasedOn"));

        var snackbarTemplates = styles
            .Where(resource => resource.Name.LocalName == "DataTemplate")
            .ToDictionary(
                resource => (string)resource.Attribute(xaml + "Key")!,
                resource => resource.Descendants().Single(element => element.Name.LocalName == "TextBlock"));
        Assert.Equal(
            "{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type ui:Snackbar}}}",
            (string?)snackbarTemplates["App.Feedback.SnackbarTitleTemplate"].Attribute("Foreground"));
        Assert.Equal(
            "{Binding ContentForeground, RelativeSource={RelativeSource AncestorType={x:Type ui:Snackbar}}}",
            (string?)snackbarTemplates["App.Feedback.SnackbarMessageTemplate"].Attribute("Foreground"));
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Progress_and_feedback_gallery_scenes_measure_with_accessible_content(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            var progressScene = GallerySceneRegistry.Build("progress");
            var feedbackScene = GallerySceneRegistry.Build("feedback");
            using var progressHost = WpfWindowHost.Show(new Window
            {
                Content = progressScene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            using var feedbackHost = WpfWindowHost.Show(new Window
            {
                Content = feedbackScene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            progressHost.Window.UpdateLayout();
            feedbackHost.Window.UpdateLayout();

            var standard = FindDescendants<ProgressBar>(progressScene).Single(control =>
                AutomationProperties.GetAutomationId(control) == "progress-standard");
            var compact = FindDescendants<ProgressBar>(progressScene).Single(control =>
                AutomationProperties.GetAutomationId(control) == "progress-compact");
            var slider = FindDescendants<Slider>(progressScene).Single(control =>
                AutomationProperties.GetAutomationId(control) == "progress-slider");
            Assert.NotSame(standard.Style, slider.Style);
            Assert.NotSame(compact.Style, slider.Style);
            Assert.True(standard.ActualHeight >= standard.MinHeight);
            Assert.True(compact.ActualHeight >= compact.MinHeight);
            Assert.True(slider.ActualHeight > 0);
            Assert.NotEmpty(AutomationProperties.GetName(standard));
            Assert.NotEmpty(AutomationProperties.GetName(slider));

            Assert.Empty(FindDescendants<ContentControl>(feedbackScene));
            Assert.NotNull(FindDescendants<Border>(feedbackScene).Single(border =>
                AutomationProperties.GetAutomationId(border) == "feedback-popup").Style);
            Assert.NotNull(FindDescendants<Border>(feedbackScene).Single(border =>
                AutomationProperties.GetAutomationId(border) == "feedback-inline").Style);
            Assert.NotNull(FindDescendants<TextBlock>(feedbackScene).Single(block =>
                AutomationProperties.GetAutomationId(block) == "feedback-validation").Style);
            Assert.NotEmpty(AutomationProperties.GetName(
                FindDescendants<TextBlock>(feedbackScene).Single(block =>
                    AutomationProperties.GetAutomationId(block) == "feedback-validation")));
        });
    }

    private static string[] ReadKeys(string path)
    {
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        return (XDocument.Load(path).Root?.Elements() ?? [])
            .Select(resource => resource.Attribute(xaml + "Key")?.Value ?? string.Empty)
            .ToArray();
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
}
