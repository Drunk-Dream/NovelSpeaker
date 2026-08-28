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
        var legacyExecutableName = string.Join('.', "NovelSpeaker", "App", "exe");
        var requiredRootFilesStart = workflow.IndexOf("$requiredRootFiles", StringComparison.Ordinal);
        var requiredRootFilesLoopStart = workflow.IndexOf(
            "foreach ($required in $requiredRootFiles)",
            requiredRootFilesStart,
            StringComparison.Ordinal);

        Assert.True(requiredRootFilesStart >= 0);
        Assert.True(requiredRootFilesLoopStart > requiredRootFilesStart);

        var requiredRootFiles = workflow[requiredRootFilesStart..requiredRootFilesLoopStart];
        Assert.Contains("'NovelSpeaker.exe'", requiredRootFiles, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{legacyExecutableName}'", requiredRootFiles, StringComparison.Ordinal);
        Assert.Contains(
            "$publishedLegacyExecutables = Get-ChildItem artifacts/publish -Recurse -File",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Where-Object { $_.Name -eq '" + legacyExecutableName + "' }",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "$packagedLegacyExecutables = $entries",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "[System.IO.Path]::GetFileName($_) -eq '" + legacyExecutableName + "'",
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
