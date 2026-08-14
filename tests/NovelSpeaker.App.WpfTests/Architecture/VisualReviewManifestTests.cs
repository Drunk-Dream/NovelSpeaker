using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Architecture;

public sealed class VisualReviewManifestTests
{
    [Fact]
    public void Root_manifest_covers_every_family_page_window_scene_and_verified_screenshot()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var visualRoot = Path.Combine(repositoryRoot, "artifacts", "visual-review");
        var rootManifestPath = Path.Combine(visualRoot, "manifest.json");
        Assert.True(File.Exists(rootManifestPath), "The root visual-review manifest is missing.");

        var childManifests = Directory
            .EnumerateFiles(visualRoot, "manifest.json", SearchOption.AllDirectories)
            .Where(path => !path.Equals(rootManifestPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (childManifests.Length == 0)
        {
            // Generated visual assets are intentionally ignored by Git. The default CI/test
            // path does not generate screenshots, so the root index is only verifiable when
            // an explicit visual-artifact generation run has supplied child manifests.
            return;
        }

        using var rootDocument = JsonDocument.Parse(File.ReadAllText(rootManifestPath));
        Assert.Equal(1, Property(rootDocument.RootElement, "schemaVersion").GetInt32());
        var actual = Property(rootDocument.RootElement, "entries")
            .EnumerateArray()
            .Select(ReadRootEntry)
            .Order()
            .ToArray();
        var expected = ReadChildEntries(visualRoot).Order().ToArray();

        Assert.NotEmpty(actual);
        Assert.Contains(actual, entry => entry.Category == "gallery");
        Assert.Contains(actual, entry => entry.Category == "pages");
        Assert.Contains(actual, entry => entry.Category == "windows");
        Assert.Equal(expected, actual);
        Assert.Equal(actual.Length, actual.Distinct().Count());
    }

    private static IEnumerable<VisualReviewEntry> ReadChildEntries(string visualRoot)
    {
        var rootManifest = Path.Combine(visualRoot, "manifest.json");
        foreach (var manifestPath in Directory
                     .EnumerateFiles(visualRoot, "manifest.json", SearchOption.AllDirectories)
                     .Where(path => !path.Equals(rootManifest, StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var artifactId = Property(document.RootElement, "artifactId").GetString()!;
            var relativeManifest = Path.GetRelativePath(visualRoot, manifestPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var category = relativeManifest.Split('/')[0];

            foreach (var scene in Property(document.RootElement, "scenes", "scenarios").EnumerateArray())
            {
                var pngName = Property(scene, "png").GetString()!;
                var pngPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, pngName);
                Assert.True(File.Exists(pngPath), $"Missing visual review screenshot: {pngPath}");
                var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pngPath))).ToLowerInvariant();
                var declaredHash = Property(scene, "sha256").GetString()!.ToLowerInvariant();
                Assert.Equal(declaredHash, actualHash);

                var scenario = TryProperty(scene, out var scenarioElement, "scenario", "scene")
                    ? scenarioElement.GetString()!
                    : "default";
                yield return new VisualReviewEntry(
                    category,
                    artifactId,
                    scenario,
                    Property(scene, "theme").GetString()!.ToLowerInvariant(),
                    Property(scene, "dpi").GetInt32(),
                    Path.GetRelativePath(visualRoot, pngPath).Replace(Path.DirectorySeparatorChar, '/'),
                    actualHash);
            }
        }
    }

    private static VisualReviewEntry ReadRootEntry(JsonElement element) =>
        new(
            Property(element, "category").GetString()!,
            Property(element, "artifactId").GetString()!,
            Property(element, "scenario").GetString()!,
            Property(element, "theme").GetString()!,
            Property(element, "dpi").GetInt32(),
            Property(element, "png").GetString()!,
            Property(element, "sha256").GetString()!);

    private static JsonElement Property(JsonElement element, params string[] names)
    {
        if (TryProperty(element, out var value, names))
        {
            return value;
        }

        throw new InvalidDataException($"Missing JSON property: {string.Join(" or ", names)}");
    }

    private static bool TryProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "artifacts")) &&
                File.Exists(Path.Combine(current.FullName, "NovelSpeaker.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the NovelSpeaker repository root.");
    }

    private sealed record VisualReviewEntry(
        string Category,
        string ArtifactId,
        string Scenario,
        string Theme,
        int Dpi,
        string Png,
        string Sha256) : IComparable<VisualReviewEntry>
    {
        public int CompareTo(VisualReviewEntry? other) =>
            other is null
                ? 1
                : StringComparer.Ordinal.Compare(ToString(), other.ToString());
    }
}
