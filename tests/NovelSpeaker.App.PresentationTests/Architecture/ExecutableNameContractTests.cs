using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class ExecutableNameContractTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();

    [Fact]
    public void App_project_uses_the_published_executable_name_without_changing_package_identity()
    {
        var project = Repository.ReadProject("src/NovelSpeaker.App/NovelSpeaker.App.csproj");

        Assert.Equal("NovelSpeaker", project.Properties["AssemblyName"]);
        Assert.Equal("NovelSpeaker.App", project.Properties["PackageId"]);
    }

    [Fact]
    public void Release_workflow_requires_the_new_name_and_rejects_the_legacy_name()
    {
        var workflow = File.ReadAllText(Path.Combine(Repository.RootPath, ".github", "workflows", "release.yml"));
        var packageValidation = File.ReadAllText(Path.Combine(Repository.RootPath, "tools", "ReleasePackageValidation.ps1"));
        var legacyExecutableName = string.Join('.', "NovelSpeaker", "App", "exe");

        Assert.Contains("'NovelSpeaker.exe'", packageValidation, StringComparison.Ordinal);
        Assert.Contains("$name -eq '" + legacyExecutableName + "'", packageValidation, StringComparison.Ordinal);
        Assert.Contains(
            "Assert-ReleasePackageDirectory -Path artifacts/publish",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Assert-ReleasePackageZip -Path $zip",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_uses_the_published_executable_name()
    {
        var readme = File.ReadAllText(Path.Combine(Repository.RootPath, "README.md"));
        var legacyExecutableName = string.Join('.', "NovelSpeaker", "App", "exe");

        Assert.Contains("`NovelSpeaker.exe`", readme, StringComparison.Ordinal);
        Assert.DoesNotContain($"`{legacyExecutableName}`", readme, StringComparison.Ordinal);
    }
}
