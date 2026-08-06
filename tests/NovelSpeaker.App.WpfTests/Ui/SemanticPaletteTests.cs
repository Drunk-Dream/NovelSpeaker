using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using NovelSpeaker.App.Shared.Theming;
using Wpf.Ui.Appearance;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class SemanticPaletteTests
{
    [Fact]
    public void Light_and_dark_palette_dictionaries_have_the_same_brush_contract()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var paletteDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Palettes");
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var dictionaries = new[] { "Palette.Light.xaml", "Palette.Dark.xaml" }
            .Select(fileName => XDocument.Load(Path.Combine(paletteDirectory, fileName)))
            .Select(document => document.Root?.Elements().ToArray() ?? [])
            .ToArray();
        var keySets = dictionaries
            .Select(resources => resources
                .Select(resource => (Key: (string?)resource.Attribute(xamlNamespace + "Key"), Type: resource.Name.LocalName))
                .ToArray())
            .ToArray();

        Assert.Equal(
            SemanticPaletteRuntime.Keys.Order(StringComparer.Ordinal),
            keySets[0].Select(resource => resource.Key!).Order(StringComparer.Ordinal));
        Assert.Equal(
            keySets[0].Select(resource => resource.Key),
            keySets[1].Select(resource => resource.Key));
        Assert.All(keySets.SelectMany(static resources => resources), resource =>
        {
            Assert.NotNull(resource.Key);
            Assert.Equal("SolidColorBrush", resource.Type);
        });
    }

    [Fact]
    public void Palette_resources_keep_main_text_and_status_combinations_readable_in_both_themes()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new WpfUiThemeRuntime();
            foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
            {
                if (theme == ApplicationTheme.Light)
                {
                    runtime.ApplyLightTheme();
                }
                else
                {
                    runtime.ApplyDarkTheme();
                }

                var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                    global::System.Windows.Application.Current);
                Assert.All(
                    SemanticPaletteRuntime.Keys,
                    key => Assert.IsType<SolidColorBrush>(application.FindResource(key)));

                AssertContrast(application, "App.Brush.Text.Primary", "App.Brush.Window.Background", 4.5);
                AssertContrast(application, "App.Brush.Text.Primary", "App.Brush.Canvas", 4.5);
                AssertContrast(application, "App.Brush.Text.Primary", "App.Brush.Surface.Primary", 4.5);
                AssertContrast(application, "App.Brush.Text.Primary", "App.Brush.Surface.Secondary", 4.5);
                AssertContrast(application, "App.Brush.Text.Primary", "App.Brush.Surface.Raised", 4.5);
                AssertContrast(application, "App.Brush.Text.Secondary", "App.Brush.Surface.Primary", 4.5);
                AssertContrast(application, "App.Brush.Text.Secondary", "App.Brush.Surface.Secondary", 4.5);
                AssertContrast(application, "App.Brush.Text.Tertiary", "App.Brush.Surface.Primary", 3.0);
                AssertContrast(application, "App.Brush.Accent.Text", "App.Brush.Accent", 4.4);
                AssertContrast(application, "App.Brush.Danger.Text", "App.Brush.Danger", 4.5);
                AssertContrast(application, "App.Brush.Danger.Pressed.Text", "App.Brush.Danger.Pressed", 4.5);
                AssertContrast(application, "App.Brush.Warning.Text", "App.Brush.Warning", 4.5);
                AssertContrast(application, "App.Brush.Success.Text", "App.Brush.Success", 4.5);
            }
        });
    }

    private static void AssertContrast(
        global::System.Windows.Application application,
        string foregroundKey,
        string backgroundKey,
        double minimum)
    {
        var foreground = Assert.IsType<SolidColorBrush>(application.FindResource(foregroundKey));
        var background = Assert.IsType<SolidColorBrush>(application.FindResource(backgroundKey));
        var ratio = ContrastRatio(foreground.Color, background.Color);

        Assert.True(
            ratio >= minimum,
            $"Expected {foregroundKey} on {backgroundKey} to reach {minimum:0.0}:1, but was {ratio:0.00}:1.");
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linearize(color.R)) +
               (0.7152 * Linearize(color.G)) +
               (0.0722 * Linearize(color.B));
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
