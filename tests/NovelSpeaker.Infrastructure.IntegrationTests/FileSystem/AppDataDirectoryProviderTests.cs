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
