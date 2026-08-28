using System.Text.Json;
using NovelSpeaker.App.PresentationTests.Architecture;
using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Bootstrap;

public sealed class LaunchProfileContractTests
{
    [Fact]
    public void Default_project_launch_profile_selects_development_environment_without_fixed_root()
    {
        var repository = ArchitectureTestRepository.Locate();
        var path = Path.Combine(
            repository.RootPath,
            "src",
            "NovelSpeaker.App",
            "Properties",
            "launchSettings.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var profiles = document.RootElement.GetProperty("profiles");
        var profile = profiles.GetProperty("NovelSpeaker.App");
        var environmentVariables = profile.GetProperty("environmentVariables");

        Assert.Equal("Project", profile.GetProperty("commandName").GetString());
        Assert.Equal(
            "Development",
            environmentVariables.GetProperty("NOVELSPEAKER_ENVIRONMENT").GetString());
        Assert.False(environmentVariables.TryGetProperty("NOVELSPEAKER_DATA_ROOT", out _));
    }

    [Fact]
    public void Test_provider_construction_does_not_use_runtime_data_roots()
    {
        var repository = ArchitectureTestRepository.Locate();
        var testRoot = Path.Combine(repository.RootPath, "tests");
        var providerConstructionMarker = "new " + nameof(AppDataDirectoryProvider) + "(";
        var violations = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                providerConstructionMarker,
                StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains(
                           "Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)",
                           StringComparison.Ordinal) ||
                       source.Contains("Path.Combine(AppContext.BaseDirectory, \"Data\")", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(repository.RootPath, path))
            .ToArray();

        Assert.Empty(violations);
    }
}
