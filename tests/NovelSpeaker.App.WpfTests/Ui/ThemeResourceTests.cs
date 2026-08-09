using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed class ThemeResourceTests
{
    [Fact]
    public void Product_xaml_does_not_hard_code_theme_colors()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var paletteRoot = Path.Combine("Shared", "Theming", "Palettes");
        var xamlFiles = Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(paletteRoot, StringComparison.Ordinal));

        Assert.All(xamlFiles, path => Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", File.ReadAllText(path)));
    }

    [Fact]
    public void Semantic_resources_reference_provider_theme_brushes_and_preserve_interaction_states()
    {
        var path = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Legacy",
            "LegacyStyles.xaml");
        var content = File.ReadAllText(path);

        Assert.Contains("TextFillColorPrimaryBrush", content);
        Assert.Contains("CardBackgroundFillColorDefaultBrush", content);
        Assert.Contains("AccentFillColorDefaultBrush", content);
        Assert.Contains("Property=\"IsMouseOver\"", content);
        Assert.Contains("Property=\"IsPressed\"", content);
        Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", content);
        Assert.DoesNotContain("Property=\"IsKeyboardFocused\"", content);
    }

    [Fact]
    public void Application_resource_dictionaries_do_not_take_over_standard_control_templates_globally()
    {
        var resourcesRoot = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources");
        var standardTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Button",
            "CheckBox",
            "ComboBox",
            "ListBox",
            "ListBoxItem",
            "Slider",
            "TextBox",
            "ToggleButton"
        };

        foreach (var path in Directory.EnumerateFiles(resourcesRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path);
            foreach (var style in document.Descendants().Where(element => element.Name.LocalName == "Style"))
            {
                var targetType = (string?)style.Attribute("TargetType") ?? string.Empty;
                Assert.False(
                    standardTypes.Contains(targetType) &&
                    style.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key") is null,
                    $"Unexpected global implicit standard-control style in {Path.GetRelativePath(GetRepositoryRoot(), path)}.");
            }
        }
    }

    [Fact]
    public void Icon_buttons_expose_tooltips_and_automation_names()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var xamlFiles = Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(static path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Shared{Path.DirectorySeparatorChar}Theming{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));
        var violations = xamlFiles.SelectMany(FindIconButtonsWithoutAccessibleMetadata).ToArray();

        Assert.True(
            violations.Length == 0,
            $"Found icon buttons without Tooltip and AutomationProperties.Name:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Cache_cleanup_text_buttons_use_short_action_labels()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var cleanupLabels = Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Select(XDocument.Load)
            .SelectMany(static document => document
                .Descendants()
                .Where(static element => element.Name.LocalName == "Button")
                .Select(static element => (string?)element.Attribute("Content")))
            .Where(static content => content?.Contains("清理", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(cleanupLabels);
        Assert.All(cleanupLabels, static label => Assert.Equal("清理", label));
    }

    [Fact]
    public void App_textblocks_explicitly_bind_to_semantic_text_styles()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var xamlFiles = Directory
            .EnumerateFiles(Path.Combine(appRoot, "Features"), "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(appRoot, "Shared"), "*.xaml", SearchOption.AllDirectories))
            .Concat([
                Path.Combine(appRoot, "Shell", "MainWindow.xaml"),
                Path.Combine(appRoot, "Bootstrap", "StartupStatusWindow.xaml")
            ]);
        var violations = xamlFiles.SelectMany(FindUnstyledTextBlocks).ToArray();

        Assert.True(
            violations.Length == 0,
            $"Found TextBlock elements without explicit semantic style or foreground:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static IEnumerable<string> FindUnstyledTextBlocks(string xamlPath)
    {
        var document = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
        var xNamespace = document.Root?.GetDefaultNamespace() ?? XNamespace.None;

        foreach (var textBlock in document.Descendants(xNamespace + "TextBlock"))
        {
            if (textBlock.Attribute("Style") is not null ||
                textBlock.Attribute("Foreground") is not null ||
                textBlock.Elements().Any(static element => element.Name.LocalName == "TextBlock.Style"))
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)textBlock;
            yield return $"{Path.GetRelativePath(GetRepositoryRoot(), xamlPath)}:{lineInfo.LineNumber}";
        }
    }

    private static IEnumerable<string> FindIconButtonsWithoutAccessibleMetadata(string xamlPath)
    {
        var document = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
        var presentationNamespace = document.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var button in document.Descendants(presentationNamespace + "Button"))
        {
            if (!button.Descendants().Any(static element => element.Name.LocalName == "SymbolIcon"))
            {
                continue;
            }

            var hasToolTip = button.Attribute("ToolTip") is not null ||
                             button.Elements().Any(static element => element.Name.LocalName == "Button.ToolTip");
            var hasAutomationName = button.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name" &&
                attribute.Name.Namespace != xamlNamespace);
            if (hasToolTip && hasAutomationName)
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)button;
            yield return $"{Path.GetRelativePath(GetRepositoryRoot(), xamlPath)}:{lineInfo.LineNumber}";
        }
    }

    private static string GetRepositoryRoot()
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
