using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovelSpeaker.App.Shared.Theming;
using Xunit;

namespace NovelSpeaker.TestKit.Wpf;

internal static class PageVisualReviewHarness
{
    public static void GenerateAndVerifyRepeatable(
        string repositoryRoot,
        string artifactId,
        IReadOnlyList<PageVisualReviewScenario> scenarios,
        Func<PageVisualReviewPage> createPage)
    {
        var outputDirectory = Path.Combine(
            repositoryRoot,
            "artifacts",
            "visual-review",
            "pages",
            artifactId);
        Directory.CreateDirectory(outputDirectory);
        var expectedGitCommit = ReadGitCommit(repositoryRoot);

        Generate(outputDirectory, artifactId, expectedGitCommit, scenarios, createPage);
        var first = ReadManifest(outputDirectory);
        AssertManifest(first, outputDirectory, artifactId, expectedGitCommit, scenarios.Count * 2);
        var firstSnapshot = JsonSerializer.Serialize(first);

        Generate(outputDirectory, artifactId, expectedGitCommit, scenarios, createPage);
        var second = ReadManifest(outputDirectory);
        AssertManifest(second, outputDirectory, artifactId, expectedGitCommit, scenarios.Count * 2);
        Assert.Equal(firstSnapshot, JsonSerializer.Serialize(second));
    }

    private static void Generate(
        string outputDirectory,
        string artifactId,
        string gitCommit,
        IReadOnlyList<PageVisualReviewScenario> scenarios,
        Func<PageVisualReviewPage> createPage)
    {
        var entries = new List<PageVisualReviewEntry>();
        var themeRuntime = new WpfUiThemeRuntime();
        var size = new Size(960, 640);

        try
        {
            foreach (var (themeName, applyTheme) in new (string Name, Action Apply)[]
                     {
                         ("light", themeRuntime.ApplyLightTheme),
                         ("dark", themeRuntime.ApplyDarkTheme)
                     })
            {
                applyTheme();
                foreach (var scenario in scenarios)
                {
                    using var fixture = createPage();
                    scenario.Configure?.Invoke(fixture.Page);
                    using var host = new WpfControlHost(fixture.Page);
                    host.MeasureArrange(size);
                    Assert.True(fixture.Page.ActualWidth > 0);
                    Assert.True(fixture.Page.ActualHeight > 0);

                    var dpi = 96 * scenario.Scale;
                    var png = EncodePng(RenderWithShellCanvas(host.Render(size, dpi), size, dpi));
                    var frame = DecodePng(png);
                    var fileName = $"{artifactId}.{scenario.Id}.{themeName}.{scenario.Scale * 100:0}.png";
                    File.WriteAllBytes(Path.Combine(outputDirectory, fileName), png);
                    entries.Add(new PageVisualReviewEntry(
                        scenario.Id,
                        themeName,
                        scenario.Scale,
                        dpi,
                        frame.PixelWidth,
                        frame.PixelHeight,
                        fileName,
                        Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()));
                }
            }

            var manifest = new PageVisualReviewManifest(
                artifactId,
                "NovelSpeaker.App.WpfTests",
                gitCommit,
                960,
                640,
                entries);
            File.WriteAllText(
                Path.Combine(outputDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            themeRuntime.ApplyLightTheme();
        }
    }

    private static BitmapSource RenderWithShellCanvas(BitmapSource page, Size size, double dpi)
    {
        var shell = new Border
        {
            Width = size.Width,
            Height = size.Height,
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)global::System.Windows.Application.Current!.FindResource("App.Radius.Large")
        };
        shell.SetResourceReference(Border.BackgroundProperty, "NavigationViewContentBackground");
        shell.SetResourceReference(Border.BorderBrushProperty, "NavigationViewContentGridBorderBrush");
        shell.Measure(size);
        shell.Arrange(new Rect(new Point(), size));
        shell.UpdateLayout();

        var pixelWidth = (int)Math.Round(size.Width * dpi / 96d);
        var pixelHeight = (int)Math.Round(size.Height * dpi / 96d);
        var shellBitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        shellBitmap.Render(shell);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var bounds = new Rect(new Point(), size);
            context.DrawImage(shellBitmap, bounds);
            context.DrawImage(page, bounds);
        }

        var composite = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        composite.Render(visual);
        composite.Freeze();
        return composite;
    }

    private static void AssertManifest(
        PageVisualReviewManifest manifest,
        string outputDirectory,
        string artifactId,
        string expectedGitCommit,
        int expectedCount)
    {
        Assert.Equal(artifactId, manifest.ArtifactId);
        Assert.Equal("NovelSpeaker.App.WpfTests", manifest.Tool);
        Assert.Equal(expectedGitCommit, manifest.GitCommit);
        Assert.Equal(960, manifest.WindowWidth);
        Assert.Equal(640, manifest.WindowHeight);
        Assert.Equal(expectedCount, manifest.Scenes.Count);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Scenes)
        {
            Assert.True(entry.Theme is "light" or "dark");
            Assert.True(keys.Add($"{entry.Scenario}|{entry.Theme}|{entry.Scale:R}"));
            Assert.Equal(96 * entry.Scale, entry.Dpi);
            Assert.Equal(32, Convert.FromHexString(entry.Sha256).Length);
            var png = File.ReadAllBytes(Path.Combine(outputDirectory, entry.Png));
            Assert.Equal(entry.Sha256, Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant());
            var frame = DecodePng(png);
            Assert.Equal(entry.Width, frame.PixelWidth);
            Assert.Equal(entry.Height, frame.PixelHeight);
            Assert.InRange(frame.DpiX, entry.Dpi - 0.1, entry.Dpi + 0.1);
            Assert.InRange(frame.DpiY, entry.Dpi - 0.1, entry.Dpi + 0.1);
        }

        Assert.Equal(expectedCount, keys.Count);
    }

    private static PageVisualReviewManifest ReadManifest(string outputDirectory)
    {
        using var stream = File.OpenRead(Path.Combine(outputDirectory, "manifest.json"));
        return JsonSerializer.Deserialize<PageVisualReviewManifest>(stream)
            ?? throw new InvalidDataException("Page visual review manifest was empty.");
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapFrame DecodePng(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        return BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    }

    private static string ReadGitCommit(string repositoryRoot)
    {
        var gitDirectory = ResolveGitDirectory(repositoryRoot);
        var head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
        var commit = head.StartsWith("ref: ", StringComparison.Ordinal)
            ? ReadGitReference(gitDirectory, head[5..].Trim())
            : head;
        if (commit is null || (commit.Length != 40 && commit.Length != 64) || !commit.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Repository HEAD does not contain a valid Git commit.");
        }

        return commit;
    }

    private static string? ReadGitReference(string gitDirectory, string referenceName)
    {
        var referencePath = Path.Combine(gitDirectory, referenceName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(referencePath))
        {
            return File.ReadAllText(referencePath).Trim();
        }

        var packedRefsPath = Path.Combine(gitDirectory, "packed-refs");
        if (!File.Exists(packedRefsPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(packedRefsPath))
        {
            if (line.StartsWith('#') || line.StartsWith('^'))
            {
                continue;
            }

            var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1].Equals(referenceName, StringComparison.Ordinal))
            {
                return parts[0];
            }
        }

        return null;
    }

    private static string ResolveGitDirectory(string repositoryRoot)
    {
        var dotGitPath = Path.Combine(repositoryRoot, ".git");
        if (Directory.Exists(dotGitPath))
        {
            return dotGitPath;
        }

        var gitDirLine = File.ReadAllText(dotGitPath).Trim();
        const string prefix = "gitdir: ";
        if (!gitDirLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new DirectoryNotFoundException("Could not locate repository Git directory.");
        }

        var gitDirectory = gitDirLine[prefix.Length..].Trim();
        return Path.GetFullPath(Path.IsPathRooted(gitDirectory)
            ? gitDirectory
            : Path.Combine(repositoryRoot, gitDirectory));
    }

    private sealed record PageVisualReviewManifest(
        string ArtifactId,
        string Tool,
        string GitCommit,
        int WindowWidth,
        int WindowHeight,
        IReadOnlyList<PageVisualReviewEntry> Scenes);

    private sealed record PageVisualReviewEntry(
        string Scenario,
        string Theme,
        double Scale,
        double Dpi,
        int Width,
        int Height,
        string Png,
        string Sha256);
}

internal sealed record PageVisualReviewScenario(
    string Id,
    double Scale,
    Action<FrameworkElement>? Configure = null);

internal sealed class PageVisualReviewPage(FrameworkElement page, Action dispose) : IDisposable
{
    public FrameworkElement Page { get; } = page;

    public void Dispose() => dispose();
}
