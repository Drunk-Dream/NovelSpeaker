using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.FileSystem;

public sealed class AppDataDirectoryProviderTests
{
    [Fact]
    public async Task EnsureCreatedAsync_creates_expected_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var provider = new AppDataDirectoryProvider(root);

        await provider.EnsureCreatedAsync(CancellationToken.None);

        Assert.True(Directory.Exists(provider.RootDirectoryPath));
        Assert.True(Directory.Exists(provider.BooksDirectoryPath));
        Assert.True(Directory.Exists(provider.CacheDirectoryPath));
        Assert.True(Directory.Exists(provider.OperationsDirectoryPath));
        Assert.True(Directory.Exists(provider.LogsDirectoryPath));
        Assert.True(Directory.Exists(provider.DiagnosticsDirectoryPath));
    }

    [DirectoryLinkTheory]
    [InlineData("Books")]
    [InlineData("Cache")]
    [InlineData("Diagnostics")]
    public async Task EnsureCreatedAsync_rejects_reparse_points_inside_data_root(string directoryName)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var provider = new AppDataDirectoryProvider(root);
        var outside = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(root);
        DirectoryLinkTestHelper.CreateDirectoryLink(Path.Combine(root, directoryName), outside);

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.EnsureCreatedAsync(CancellationToken.None));
    }

    [Fact]
    public void Constructor_exposes_all_paths_under_the_injected_root()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var provider = new AppDataDirectoryProvider(root);

        Assert.Equal(Path.GetFullPath(root), provider.RootDirectoryPath);
        Assert.Equal(Path.Combine(provider.RootDirectoryPath, "app.db"), provider.DatabasePath);
        Assert.Equal(Path.Combine(provider.RootDirectoryPath, "settings.json"), provider.SettingsPath);
        Assert.Equal(Path.Combine(provider.RootDirectoryPath, "Books"), provider.BooksDirectoryPath);
        Assert.Equal(Path.Combine(provider.RootDirectoryPath, "Cache"), provider.CacheDirectoryPath);
        Assert.Equal(Path.Combine(provider.RootDirectoryPath, "Operations"), provider.OperationsDirectoryPath);
        Assert.Equal(Path.Combine(provider.RootDirectoryPath, "Logs"), provider.LogsDirectoryPath);
    }
}
