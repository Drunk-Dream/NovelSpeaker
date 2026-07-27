using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.FileSystem;

public sealed class LocalAppDataDirectoryProviderTests
{
    [Fact]
    public async Task EnsureCreatedAsync_creates_expected_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var provider = new LocalAppDataDirectoryProvider(root);

        await provider.EnsureCreatedAsync(CancellationToken.None);

        Assert.True(Directory.Exists(provider.RootDirectoryPath));
        Assert.True(Directory.Exists(provider.BooksDirectoryPath));
        Assert.True(Directory.Exists(provider.CacheDirectoryPath));
        Assert.True(Directory.Exists(provider.LogsDirectoryPath));
    }

    [Fact]
    public void Constructor_exposes_expected_database_path()
    {
        var root = Path.Combine("C:\\Temp", "NovelSpeaker");
        var provider = new LocalAppDataDirectoryProvider(root);

        Assert.Equal(Path.Combine(root, "app.db"), provider.DatabasePath);
    }
}
