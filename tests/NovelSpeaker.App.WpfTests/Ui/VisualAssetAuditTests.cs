using System.IO;
using System.Text.Json;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed class VisualAssetAuditTests
{
    [Fact]
    public void Audit_manifest_covers_every_application_xaml_file()
    {
        using var document = LoadManifest();
        var root = document.RootElement;
        var repositoryRoot = GetRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "NovelSpeaker.App");

        var declaredSources = root
            .GetProperty("xamlAssets")
            .EnumerateArray()
            .Select(static asset => asset.GetProperty("source").GetString())
            .Where(static source => source is not null)
            .Select(static source => source!.Replace('\\', '/'))
            .OrderBy(static source => source, StringComparer.Ordinal)
            .ToArray();
        var actualSources = Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .OrderBy(static source => source, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(actualSources, declaredSources);

        foreach (var asset in root.GetProperty("xamlAssets").EnumerateArray())
        {
            var id = asset.GetProperty("id").GetString();
            var source = asset.GetProperty("source").GetString();
            var rootType = asset.GetProperty("root").GetString();
            var contracts = asset.GetProperty("behaviorContracts").EnumerateArray().ToArray();

            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.False(string.IsNullOrWhiteSpace(rootType));
            Assert.NotNull(source);
            Assert.True(File.Exists(Path.Combine(repositoryRoot, source!)), source);
            Assert.NotEmpty(contracts);
        }
    }

    [Fact]
    public void Audit_manifest_assigns_every_visual_finding_a_concrete_migration_target()
    {
        using var document = LoadManifest();
        var findings = document.RootElement.GetProperty("migrationFindings").EnumerateArray().ToArray();
        var requiredCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            "color",
            "corner-radius",
            "shadow",
            "button-template",
            "text-box-template",
            "slider-template",
            "list-selection-style"
        };

        Assert.NotEmpty(findings);
        Assert.True(
            requiredCategories.IsSubsetOf(
                findings
                    .Select(static finding => finding.GetProperty("category").GetString())
                    .OfType<string>()),
            "The audit must explicitly cover colors, corner radii, shadows, Button/TextBox/Slider templates and list selection styles.");

        foreach (var finding in findings)
        {
            var id = finding.GetProperty("id").GetString();
            var current = finding.GetProperty("current").GetString();
            var migrationTarget = finding.GetProperty("migrationTarget").GetString();

            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.False(string.IsNullOrWhiteSpace(current));
            Assert.False(string.IsNullOrWhiteSpace(migrationTarget));

            var target = migrationTarget!;
            Assert.DoesNotContain("TODO", target, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TBD", target, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("以后", target, StringComparison.Ordinal);
            Assert.DoesNotContain("待定", target, StringComparison.Ordinal);

            foreach (var source in finding
                         .GetProperty("sources")
                         .EnumerateArray()
                         .Select(static item => item.GetString())
                         .Where(static source => source is not null))
            {
                Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), source!)), source);
            }
        }
    }

    [Fact]
    public void Audit_manifest_records_theme_accent_replacement_and_feedback_owners()
    {
        using var document = LoadManifest();
        var root = document.RootElement;
        var theme = root.GetProperty("themeEntry");

        Assert.Contains("App.xaml", theme.GetProperty("initialDictionary").GetString());
        Assert.Contains("WpfUiThemeRuntime.cs", theme.GetProperty("runtimeReplacement").GetString());
        Assert.Contains("ApplicationThemeManager", theme.GetProperty("runtimeReplacement").GetString());
        Assert.Contains("ThemePreferenceService.cs", theme.GetProperty("persistedPreference").GetString());
        Assert.Contains("AccentBrush", theme.GetProperty("accentSource").GetString());
        Assert.Contains("DynamicResource", theme.GetProperty("synchronization").GetString());

        var runtimeAssets = root.GetProperty("runtimeAssets").EnumerateArray().ToArray();
        var runtimeIds = runtimeAssets
            .Select(static asset => asset.GetProperty("id").GetString())
            .Where(static id => id is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("confirmation-dialog", runtimeIds);
        Assert.Contains("book-delete-dialog", runtimeIds);
        Assert.Contains("import-progress-dialog", runtimeIds);
        Assert.Contains("active-cache-flyout", runtimeIds);
        Assert.Contains("root-snackbar", runtimeIds);
        Assert.Contains("context-menus", runtimeIds);

        foreach (var asset in runtimeAssets)
        {
            var migrationTarget = asset.GetProperty("migrationTarget").GetString();
            Assert.False(string.IsNullOrWhiteSpace(migrationTarget));
            Assert.DoesNotContain("以后", migrationTarget, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Audit_behavior_baseline_points_to_existing_test_files()
    {
        using var document = LoadManifest();
        var repositoryRoot = GetRepositoryRoot();

        foreach (var testPath in document.RootElement
                     .GetProperty("behaviorBaseline")
                     .EnumerateArray()
                     .SelectMany(static baseline => baseline
                         .GetProperty("tests")
                         .EnumerateArray()
                         .Select(static test => test.GetString()))
                     .Where(static testPath => testPath is not null))
        {
            Assert.True(File.Exists(Path.Combine(repositoryRoot, testPath!)), testPath);
        }
    }

    private static JsonDocument LoadManifest()
    {
        var path = Path.Combine(GetRepositoryRoot(), "docs", "VISUAL_ASSET_AUDIT.json");
        return JsonDocument.Parse(File.ReadAllText(path));
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
