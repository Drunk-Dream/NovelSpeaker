using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        Func<WindowVisualReviewWindow> createWindow,
        bool useActualClientSize = false)
    {
        var outputDirectory = Path.Combine(
            repositoryRoot,
            "artifacts",
            "visual-review",
            "windows",
            artifactId);
        Directory.CreateDirectory(outputDirectory);
        var expectedGitCommit = ReadGitCommit(repositoryRoot);

        Generate(outputDirectory, artifactId, expectedGitCommit, width, height, scenarios, createWindow, useActualClientSize);
        var first = ReadManifest(outputDirectory);
        AssertManifest(first, outputDirectory, artifactId, expectedGitCommit, width, height, scenarios.Count * 2);
        var firstSnapshot = CreateLayoutSnapshot(first);

        Generate(outputDirectory, artifactId, expectedGitCommit, width, height, scenarios, createWindow, useActualClientSize);
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
        Func<WindowVisualReviewWindow> createWindow,
        bool useActualClientSize)
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
                    fixture.Window.WindowStartupLocation = WindowStartupLocation.Manual;
                    fixture.Window.Left = 0;
                    fixture.Window.Top = 0;
                    scenario.Configure?.Invoke(fixture.Window);
                    WpfWindowHost.Show(fixture.Window);
                    fixture.PrepareAfterShow?.Invoke();
                    DrainDispatcher(fixture.Window.Dispatcher);
                    fixture.StabilizeBeforeCapture?.Invoke();
                    DrainDispatcher(fixture.Window.Dispatcher);
                    Keyboard.ClearFocus();
                    DrainDispatcher(fixture.Window.Dispatcher);
                    applyTheme();
                    fixture.Window.UpdateLayout();
                    WaitForStableRendering(
                        Assert.IsAssignableFrom<FrameworkElement>(fixture.Window.Content),
                        scenario.Id);
                    var content = Assert.IsAssignableFrom<FrameworkElement>(fixture.Window.Content);
                    var renderWidth = useActualClientSize ? content.ActualWidth : width;
                    var renderHeight = useActualClientSize ? content.ActualHeight : height;
                    Assert.True(renderWidth > 0);
                    Assert.True(renderHeight > 0);
                    var dpi = 96 * scenario.Scale;
                    var popupLayers = TransientPopupVisualRenderer.CaptureOpenLayers(
                        content,
                        dpi);
                    if (scenario.RequireTransientPopup)
                    {
                        Assert.NotEmpty(popupLayers);
                        Assert.All(popupLayers, layer => AssertLayerIntersectsViewport(
                            scenario.Id,
                            layer,
                            new Size(renderWidth, renderHeight)));
                    }
                    fixture.Window.Content = null;
                    var shell = new Border
                    {
                        Child = content,
                        DataContext = fixture.Window.DataContext
                    };
                    shell.SetResourceReference(Border.BackgroundProperty, "App.Brush.Window.Background");
                    VisualTreeHelper.SetRootDpi(shell, new DpiScale(scenario.Scale, scenario.Scale));
                    using var host = new WpfControlHost(shell);
                    host.MeasureArrange(new Size(renderWidth, renderHeight));
                    var layoutDpi = VisualTreeHelper.GetDpi(shell);
                    Assert.Equal(scenario.Scale, layoutDpi.DpiScaleX, 3);
                    Assert.Equal(scenario.Scale, layoutDpi.DpiScaleY, 3);

                    var renderSize = new Size(renderWidth, renderHeight);
                    var background = host.Render(renderSize, dpi);
                    var png = EncodePng(TransientPopupVisualRenderer.Composite(
                        background,
                        renderSize,
                        dpi,
                        popupLayers));
                    var frame = DecodePng(png);
                    var fileName = $"{artifactId}.{scenario.Id}.{themeName}.{scenario.Scale * 100:0}.png";
                    File.WriteAllBytes(Path.Combine(outputDirectory, fileName), png);
                    entries.Add(new WindowVisualReviewEntry(
                        scenario.Id,
                        themeName,
                        scenario.Scale,
                        dpi,
                        renderWidth,
                        renderHeight,
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

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void WaitForStableRendering(FrameworkElement root, string scenarioId)
    {
        const int maximumFrameCount = 180;
        byte[]? previousPixels = null;
        var stableFrameCount = 0;
        var renderedFrameCount = 0;
        var converged = false;
        var frame = new DispatcherFrame();
        EventHandler? rendering = null;
        rendering = (_, _) =>
        {
            root.UpdateLayout();
            var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);
            stableFrameCount = previousPixels is not null && previousPixels.AsSpan().SequenceEqual(pixels)
                ? stableFrameCount + 1
                : 0;
            previousPixels = pixels;
            renderedFrameCount++;
            if (stableFrameCount >= 2)
            {
                converged = true;
                frame.Continue = false;
                return;
            }

            if (renderedFrameCount >= maximumFrameCount)
            {
                frame.Continue = false;
            }
        };
        try
        {
            CompositionTarget.Rendering += rendering;
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            CompositionTarget.Rendering -= rendering;
        }

        Assert.True(
            converged,
            $"Scenario '{scenarioId}' did not reach two stable render frames within {maximumFrameCount} frames.");
    }

    private static void AssertLayerIntersectsViewport(
        string scenarioId,
        TransientPopupLayer layer,
        Size viewport)
    {
        Assert.True(
            new Rect(new Point(), viewport).Contains(new Rect(layer.Origin, layer.Size)),
            $"Scenario '{scenarioId}' captured a transient layer outside the viewport: {layer.Origin} {layer.Size}.");
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
                entry.ContentWidth,
                entry.ContentHeight,
                entry.Width,
                entry.Height,
                entry.Png,
                entry.Sha256
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
            Assert.Equal((int)Math.Ceiling(entry.ContentWidth * entry.Scale), frame.PixelWidth);
            Assert.Equal((int)Math.Ceiling(entry.ContentHeight * entry.Scale), frame.PixelHeight);
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
        double ContentWidth,
        double ContentHeight,
        int Width,
        int Height,
        string Png,
        string Sha256);
}

internal sealed record WindowVisualReviewScenario(
    string Id,
    double Scale,
    Action<Window>? Configure = null,
    bool RequireTransientPopup = false);

internal sealed class WindowVisualReviewWindow(
    Window window,
    Action dispose,
    Action? prepareAfterShow = null,
    Action? stabilizeBeforeCapture = null) : IDisposable
{
    public Window Window { get; } = window;

    public Action? PrepareAfterShow { get; } = prepareAfterShow;

    public Action? StabilizeBeforeCapture { get; } = stabilizeBeforeCapture;

    public void Dispose()
    {
        if (Window.IsVisible)
        {
            Window.Close();
        }

        dispose();
    }
}
