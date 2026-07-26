using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed class ThemeResourceTests
{
    [Fact]
    public void App_xaml_files_do_not_contain_fixed_hex_colors()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");

        foreach (var relativePath in new[]
                 {
                     Path.Combine("Shared", "Theming", "Resources", "SemanticStyles.xaml"),
                     Path.Combine("Bootstrap", "StartupStatusWindow.xaml"),
                     Path.Combine("Shell", "MainWindow.xaml"),
                     Path.Combine("Features", "Playback", "Components", "PlayerView.xaml")
                 })
        {
            var content = File.ReadAllText(Path.Combine(appRoot, relativePath));
            Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", content);
        }
    }

    [Fact]
    public void Semantic_styles_bind_to_wpf_ui_theme_resources()
    {
        var semanticStylesPath = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "SemanticStyles.xaml");
        var content = File.ReadAllText(semanticStylesPath);

        Assert.Contains("TextFillColorPrimaryBrush", content);
        Assert.Contains("TextFillColorSecondaryBrush", content);
        Assert.Contains("CardBackgroundFillColorDefaultBrush", content);
        Assert.Contains("CardStrokeColorDefaultBrush", content);
        Assert.Contains("SystemFillColorCriticalBrush", content);
    }

    [Fact]
    public void Borderless_button_styles_keep_theme_backed_interaction_states()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "SemanticStyles.xaml"));

        Assert.Contains("x:Key=\"BorderlessIconButtonStyle\"", content);
        Assert.Contains("x:Key=\"BorderlessListItemButtonStyle\"", content);
        Assert.Contains("Property=\"IsMouseOver\"", content);
        Assert.Contains("Property=\"IsPressed\"", content);
        Assert.Contains("Property=\"IsKeyboardFocused\"", content);
        Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", content);
        Assert.Contains("AccentFillColorDefaultBrush", content);
    }

    [Fact]
    public void Icon_and_list_buttons_use_shared_semantic_styles()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var playerView = File.ReadAllText(Path.Combine(appRoot, "Features", "Playback", "Components", "PlayerView.xaml"));
        var chapterRulesPage = File.ReadAllText(Path.Combine(appRoot, "Features", "ChapterRules", "ChapterRulesPage.xaml"));
        var libraryPage = File.ReadAllText(Path.Combine(appRoot, "Features", "Library", "LibraryPage.xaml"));
        var bookCardView = File.ReadAllText(Path.Combine(appRoot, "Features", "Library", "BookCardView.xaml"));

        Assert.Contains("ToolbarValueButtonStyle", playerView);
        Assert.Contains("PrimaryPlaybackIconButtonStyle", playerView);
        Assert.Contains("FloatingIconButtonStyle", playerView);
        Assert.Contains("BorderlessIconButtonStyle", libraryPage);
        Assert.Contains("BorderlessListItemButtonStyle", bookCardView);
        Assert.Contains("ReOrder24", chapterRulesPage);
        Assert.Contains("AutomationProperties.Name=\"上移\"", chapterRulesPage);
        Assert.Contains("AutomationProperties.Name=\"下移\"", chapterRulesPage);
    }

    [Fact]
    public void App_textblocks_explicitly_bind_to_semantic_text_styles()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var xamlFiles = Directory
            .EnumerateFiles(Path.Combine(appRoot, "Features"), "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(appRoot, "Shared"), "*.xaml", SearchOption.AllDirectories))
            .Concat(new[]
            {
                Path.Combine(appRoot, "Shell", "MainWindow.xaml"),
                Path.Combine(appRoot, "Bootstrap", "StartupStatusWindow.xaml")
            });

        var violations = xamlFiles
            .SelectMany(FindUnstyledTextBlocks)
            .ToArray();

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
