using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace NovelSpeaker.StyleGallery;

public sealed class GalleryScreenshotGenerator
{
    public async Task<GalleryManifest> GenerateAsync(
        GalleryWindow window,
        GalleryCommandLineOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        var themes = options.Theme == GalleryThemeChoice.All
            ? new[] { GalleryTheme.Light, GalleryTheme.Dark }
            : new[] { options.Theme.ToGalleryTheme() };
        var scenes = options.SceneName is null
            ? GallerySceneRegistry.All
            : GallerySceneRegistry.All.Where(scene =>
                scene.Name.Equals(options.SceneName, StringComparison.Ordinal)).ToArray();

        if (scenes.Count == 0)
        {
            throw new GalleryUsageException($"Unknown Style Gallery scene '{options.SceneName}'.");
        }

        Directory.CreateDirectory(options.OutputDirectory);
        var entries = new List<GalleryManifestEntry>(themes.Length * scenes.Count);

        foreach (var theme in themes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GalleryThemeRuntime.Apply(theme);

            foreach (var scene in scenes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = scene.Create();
                window.Content = root;
                await window.Dispatcher.InvokeAsync(
                    () => root.UpdateLayout(),
                    System.Windows.Threading.DispatcherPriority.Render,
                    cancellationToken);

                var bitmap = GallerySceneRenderer.Render(root, scene);
                var png = EncodePng(bitmap);
                var fileName = $"{scene.Name}.{theme.FileName()}.png";
                var fullPath = Path.Combine(options.OutputDirectory, fileName);
                await File.WriteAllBytesAsync(fullPath, png, cancellationToken);

                entries.Add(new GalleryManifestEntry(
                    scene.Name,
                    theme.ToString(),
                    scene.Width,
                    scene.Height,
                    GalleryRenderSettings.Dpi,
                    fileName,
                    Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()));
            }
        }

        var manifest = new GalleryManifest(
            "03",
            "NovelSpeaker.StyleGallery",
            GalleryRenderSettings.WindowWidth,
            GalleryRenderSettings.WindowHeight,
            GalleryRenderSettings.Dpi,
            entries);
        var json = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        await File.WriteAllBytesAsync(
            Path.Combine(options.OutputDirectory, "manifest.json"),
            json,
            cancellationToken);
        return manifest;
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

public sealed record GalleryManifest(
    string Task,
    string Tool,
    int WindowWidth,
    int WindowHeight,
    int Dpi,
    IReadOnlyList<GalleryManifestEntry> Scenes);

public sealed record GalleryManifestEntry(
    string Scene,
    string Theme,
    int Width,
    int Height,
    int Dpi,
    string Png,
    string Sha256);
