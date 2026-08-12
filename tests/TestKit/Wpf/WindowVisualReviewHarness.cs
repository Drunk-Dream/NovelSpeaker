using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NovelSpeaker.App.Shared.Theming;
using Xunit;

namespace NovelSpeaker.TestKit.Wpf;

internal static class WindowVisualReviewHarness
{
    public static void GenerateAndVerifyRepeatable(
        string repositoryRoot,
        string artifactId,
        int width,
        int height,
        IReadOnlyList<WindowVisualReviewScenario> scenarios,
        Func<WindowVisualReviewWindow> createWindow)
    {
        var outputDirectory = Path.Combine(
            repositoryRoot,
            "artifacts",
            "visual-review",
            "windows",
            artifactId);
        Directory.CreateDirectory(outputDirectory);
        var expectedGitCommit = ReadGitCommit(repositoryRoot);

        Generate(outputDirectory, artifactId, expectedGitCommit, width, height, scenarios, createWindow);
        var first = ReadManifest(outputDirectory);
        AssertManifest(first, outputDirectory, artifactId, expectedGitCommit, width, height, scenarios.Count * 2);
        var firstSnapshot = CreateLayoutSnapshot(first);

        Generate(outputDirectory, artifactId, expectedGitCommit, width, height, scenarios, createWindow);
        var second = ReadManifest(outputDirectory);
        AssertManifest(second, outputDirectory, artifactId, expectedGitCommit, width, height, scenarios.Count * 2);
        Assert.Equal(firstSnapshot, CreateLayoutSnapshot(second));
    }

    private static void Generate(
        string outputDirectory,
        string artifactId,
        string gitCommit,
        int width,
        int height,
        IReadOnlyList<WindowVisualReviewScenario> scenarios,
        Func<WindowVisualReviewWindow> createWindow)
    {
        var entries = new List<WindowVisualReviewEntry>();
        var themeRuntime = new WpfUiThemeRuntime();

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
                    using var fixture = createWindow();
                    fixture.Window.Width = width;
                    fixture.Window.Height = height;
                    scenario.Configure?.Invoke(fixture.Window);
                    WpfWindowHost.Show(fixture.Window);
                    fixture.PrepareAfterShow?.Invoke();
                    fixture.Window.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                    fixture.Window.UpdateLayout();
                    var content = Assert.IsAssignableFrom<FrameworkElement>(fixture.Window.Content);
                    fixture.Window.Content = null;
                    var shell = new Border
                    {
                        Child = content,
                        DataContext = fixture.Window.DataContext
                    };
                    shell.SetResourceReference(Border.BackgroundProperty, "App.Brush.Window.Background");
                    using var host = new WpfControlHost(shell);
                    host.MeasureArrange(new Size(width, height));

                    var dpi = 96 * scenario.Scale;
                    var png = EncodePng(host.Render(new Size(width, height), dpi));
                    var frame = DecodePng(png);
                    var fileName = $"{artifactId}.{scenario.Id}.{themeName}.{scenario.Scale * 100:0}.png";
                    File.WriteAllBytes(Path.Combine(outputDirectory, fileName), png);
                    entries.Add(new WindowVisualReviewEntry(
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

            File.WriteAllText(
                Path.Combine(outputDirectory, "manifest.json"),
                JsonSerializer.Serialize(
                    new WindowVisualReviewManifest(
                        artifactId,
                        "NovelSpeaker.App.WpfTests",
                        gitCommit,
                        width,
                        height,
                        entries),
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            themeRuntime.ApplyLightTheme();
        }
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

    private static WindowVisualReviewManifest ReadManifest(string outputDirectory)
    {
        using var stream = File.OpenRead(Path.Combine(outputDirectory, "manifest.json"));
        return JsonSerializer.Deserialize<WindowVisualReviewManifest>(stream)
            ?? throw new InvalidDataException("Window visual review manifest was empty.");
    }

    private static string CreateLayoutSnapshot(WindowVisualReviewManifest manifest) =>
        JsonSerializer.Serialize(new
        {
            manifest.ArtifactId,
            manifest.Tool,
            manifest.GitCommit,
            manifest.WindowWidth,
            manifest.WindowHeight,
            Scenes = manifest.Scenes.Select(entry => new
            {
                entry.Scenario,
                entry.Theme,
                entry.Scale,
                entry.Dpi,
                entry.Width,
                entry.Height,
                entry.Png
            })
        });

    private static void AssertManifest(
        WindowVisualReviewManifest manifest,
        string outputDirectory,
        string artifactId,
        string expectedGitCommit,
        int width,
        int height,
        int expectedCount)
    {
        Assert.Equal(artifactId, manifest.ArtifactId);
        Assert.Equal(expectedGitCommit, manifest.GitCommit);
        Assert.Equal(width, manifest.WindowWidth);
        Assert.Equal(height, manifest.WindowHeight);
        Assert.Equal(expectedCount, manifest.Scenes.Count);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Scenes)
        {
            Assert.True(keys.Add($"{entry.Scenario}|{entry.Theme}|{entry.Scale:R}"));
            Assert.Equal(96 * entry.Scale, entry.Dpi);
            var png = File.ReadAllBytes(Path.Combine(outputDirectory, entry.Png));
            Assert.Equal(entry.Sha256, Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant());
            var frame = DecodePng(png);
            Assert.Equal(entry.Width, frame.PixelWidth);
            Assert.Equal(entry.Height, frame.PixelHeight);
            Assert.Equal((int)Math.Ceiling(width * entry.Scale), frame.PixelWidth);
            Assert.Equal((int)Math.Ceiling(height * entry.Scale), frame.PixelHeight);
            Assert.InRange(frame.DpiX, entry.Dpi - 0.1, entry.Dpi + 0.1);
            Assert.InRange(frame.DpiY, entry.Dpi - 0.1, entry.Dpi + 0.1);
        }
    }

    private static string ReadGitCommit(string repositoryRoot)
    {
        var gitDirectory = Path.Combine(repositoryRoot, ".git");
        var head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
        {
            return head;
        }

        var referenceName = head[5..].Trim();
        var referencePath = Path.Combine(gitDirectory, referenceName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(referencePath))
        {
            return File.ReadAllText(referencePath).Trim();
        }

        foreach (var line in File.ReadLines(Path.Combine(gitDirectory, "packed-refs")))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1].Equals(referenceName, StringComparison.Ordinal))
            {
                return parts[0];
            }
        }

        throw new InvalidDataException("Repository HEAD does not contain a readable Git commit.");
    }

    private sealed record WindowVisualReviewManifest(
        string ArtifactId,
        string Tool,
        string GitCommit,
        int WindowWidth,
        int WindowHeight,
        IReadOnlyList<WindowVisualReviewEntry> Scenes);

    private sealed record WindowVisualReviewEntry(
        string Scenario,
        string Theme,
        double Scale,
        double Dpi,
        int Width,
        int Height,
        string Png,
        string Sha256);
}

internal sealed record WindowVisualReviewScenario(
    string Id,
    double Scale,
    Action<Window>? Configure = null);

internal sealed class WindowVisualReviewWindow(
    Window window,
    Action dispose,
    Action? prepareAfterShow = null) : IDisposable
{
    public Window Window { get; } = window;

    public Action? PrepareAfterShow { get; } = prepareAfterShow;

    public void Dispose()
    {
        if (Window.IsVisible)
        {
            Window.Close();
        }

        dispose();
    }
}
