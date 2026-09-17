using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.FileSystem;

public sealed class AppStoragePathResolverTests
{
    [Fact]
    public void ResolvePath_allows_reparse_points_above_the_data_root()
    {
        var installationRoot = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Scoop-" + Path.GetRandomFileName());
        var versionDirectory = Path.Combine(installationRoot, "1.0.0");
        var currentDirectory = Path.Combine(installationRoot, "current");
        Directory.CreateDirectory(versionDirectory);
        try
        {
            Directory.CreateSymbolicLink(currentDirectory, versionDirectory);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var directories = new AppDataDirectoryProvider(Path.Combine(currentDirectory, "Data"));
        Directory.CreateDirectory(directories.RootDirectoryPath);
        var resolver = new AppStoragePathResolver(directories);

        Assert.Equal(
            Path.Combine(directories.BooksDirectoryPath, "book-1", "content.txt"),
            resolver.ResolvePath("Books/book-1/content.txt"));
    }

    [Fact]
    public void ResolvePath_accepts_storage_key_and_legacy_path_under_root()
    {
        var directories = CreateDirectories();
        var resolver = new AppStoragePathResolver(directories);
        var expected = Path.Combine(directories.BooksDirectoryPath, "book-1", "content.txt");

        Assert.Equal(expected, resolver.ResolvePath("Books/book-1/content.txt"));
        Assert.Equal(expected, resolver.ResolvePath(expected));
        Assert.Equal("Books/book-1/content.txt", resolver.GetStorageKey(expected));
    }

    [Fact]
    public void ResolvePath_rejects_parent_traversal()
    {
        foreach (var path in new[] { "../outside.txt", "Books/../../outside.txt" })
        {
            var resolver = new AppStoragePathResolver(CreateDirectories());

            Assert.Throws<InvalidDataException>(() => resolver.ResolvePath(path));
        }
    }

    [Fact]
    public void ResolvePath_rejects_legacy_absolute_path_outside_root()
    {
        var directories = CreateDirectories();
        var resolver = new AppStoragePathResolver(directories);
        var outside = Path.Combine(Path.GetDirectoryName(directories.RootDirectoryPath)!, "external.txt");

        Assert.Throws<InvalidDataException>(() => resolver.ResolvePath(outside));
    }

    [Fact]
    public async Task ResolvePath_rejects_existing_symbolic_link_component_when_supported()
    {
        var directories = CreateDirectories();
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var outside = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outside);
        var link = Path.Combine(directories.BooksDirectoryPath, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var resolver = new AppStoragePathResolver(directories);
        Assert.Throws<InvalidDataException>(() => resolver.ResolvePath("Books/linked/content.txt"));
    }

    private static AppDataDirectoryProvider CreateDirectories() =>
        new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
}
