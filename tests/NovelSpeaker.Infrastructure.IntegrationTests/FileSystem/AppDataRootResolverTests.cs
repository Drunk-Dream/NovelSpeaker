using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.FileSystem;

public sealed class AppDataRootResolverTests
{
    [Fact]
    public void ResolveRootDirectoryPath_uses_application_data_for_formal_environment()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "formal-base");
        var localAppDataDirectory = Path.Combine(Path.GetTempPath(), "local-app-data");
        var resolver = CreateResolver(baseDirectory, localAppDataDirectory);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(baseDirectory), "Data"),
            resolver.ResolveRootDirectoryPath());
    }

    [Fact]
    public void ResolveRootDirectoryPath_uses_development_local_app_data()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "formal-base");
        var localAppDataDirectory = Path.Combine(Path.GetTempPath(), "local-app-data");
        var resolver = CreateResolver(
            baseDirectory,
            localAppDataDirectory,
            (AppDataRootResolver.EnvironmentEnvironmentVariable, AppDataRootResolver.DevelopmentEnvironmentName));

        Assert.Equal(
            Path.Combine(Path.GetFullPath(localAppDataDirectory), AppDataRootResolver.DevelopmentDirectoryName),
            resolver.ResolveRootDirectoryPath());
    }

    [Fact]
    public void ResolveRootDirectoryPath_prefers_explicit_root_over_development_environment()
    {
        var explicitRoot = Path.Combine(Path.GetTempPath(), "explicit-root");
        var resolver = CreateResolver(
            Path.Combine(Path.GetTempPath(), "formal-base"),
            Path.Combine(Path.GetTempPath(), "local-app-data"),
            (AppDataRootResolver.DataRootEnvironmentVariable, explicitRoot),
            (AppDataRootResolver.EnvironmentEnvironmentVariable, AppDataRootResolver.DevelopmentEnvironmentName));

        Assert.Equal(Path.GetFullPath(explicitRoot), resolver.ResolveRootDirectoryPath());
    }

    [Fact]
    public void ResolveRootDirectoryPath_is_not_changed_by_build_configuration()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "formal-base");
        var localAppDataDirectory = Path.Combine(Path.GetTempPath(), "local-app-data");
        var formalResolver = CreateResolver(baseDirectory, localAppDataDirectory);
        var releaseResolver = CreateResolver(baseDirectory, localAppDataDirectory);

        Assert.Equal(
            formalResolver.ResolveRootDirectoryPath(),
            releaseResolver.ResolveRootDirectoryPath());
    }

    private static AppDataRootResolver CreateResolver(
        string baseDirectory,
        string localAppDataDirectory,
        params (string Name, string Value)[] variables)
    {
        var environment = variables.ToDictionary(
            variable => variable.Name,
            variable => variable.Value,
            StringComparer.Ordinal);

        return new AppDataRootResolver(
            baseDirectory,
            localAppDataDirectory,
            name => environment.GetValueOrDefault(name));
    }
}
