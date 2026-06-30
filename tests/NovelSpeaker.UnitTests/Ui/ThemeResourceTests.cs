using System.IO;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed class ThemeResourceTests
{
    [Fact]
    public void App_xaml_files_do_not_contain_fixed_hex_colors()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");

        foreach (var relativePath in new[]
                 {
                     Path.Combine("Resources", "SemanticStyles.xaml"),
                     "StartupStatusWindow.xaml",
                     "MainWindow.xaml",
                     Path.Combine("Views", "PlayerView.xaml")
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
            "Resources",
            "SemanticStyles.xaml");
        var content = File.ReadAllText(semanticStylesPath);

        Assert.Contains("TextFillColorPrimaryBrush", content);
        Assert.Contains("TextFillColorSecondaryBrush", content);
        Assert.Contains("CardBackgroundFillColorDefaultBrush", content);
        Assert.Contains("CardStrokeColorDefaultBrush", content);
        Assert.Contains("SystemFillColorCriticalBrush", content);
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
