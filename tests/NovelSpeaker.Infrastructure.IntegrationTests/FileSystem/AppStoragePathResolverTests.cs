using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.FileSystem;

public sealed class AppStoragePathResolverTests
{
    [DirectoryLinkFact]
    public void ResolvePath_allows_reparse_points_above_the_data_root()
    {
        var installationRoot = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Scoop-" + Path.GetRandomFileName());
        var versionDirectory = Path.Combine(installationRoot, "1.0.0");
        var currentDirectory = Path.Combine(installationRoot, "current");
        Directory.CreateDirectory(versionDirectory);
        DirectoryLinkTestHelper.CreateDirectoryLink(currentDirectory, versionDirectory);

        var directories = new AppDataDirectoryProvider(Path.Combine(currentDirectory, "Data"));
        Directory.CreateDirectory(directories.RootDirectoryPath);
        var resolver = new AppStoragePathResolver(directories);

        Assert.Equal(
            Path.Combine(directories.BooksDirectoryPath, "book-1", "content.txt"),
            resolver.ResolvePath("Books/book-1/content.txt"));
    }

    [DirectoryLinkFact]
    public async Task ResolvePath_allows_the_data_root_itself_to_be_a_symbolic_link()
    {
        var installationRoot = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Scoop-" + Path.GetRandomFileName());
        var logicalDataRoot = Path.Combine(installationRoot, "current", "Data");
        var persistedDataRoot = Path.Combine(installationRoot, "persist", "novelspeaker", "Data");
        Directory.CreateDirectory(persistedDataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(logicalDataRoot)!);
        DirectoryLinkTestHelper.CreateDirectoryLink(logicalDataRoot, persistedDataRoot);

        var directories = new AppDataDirectoryProvider(logicalDataRoot);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var resolver = new AppStoragePathResolver(directories);

        var paths = new[]
        {
            "Books/book-1/content.txt",
            "Cache/cache.db",
            "settings.json",
            "Logs/telemetry.jsonl",
            "Diagnostics/session-test.nsdiag"
        };
        foreach (var storageKey in paths)
        {
            var path = resolver.ResolvePath(storageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "test");
            Assert.True(File.Exists(path));
        }

        Assert.Equal(directories.DatabasePath, resolver.ResolvePath(directories.DatabasePath));
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

    [DirectoryLinkFact]
    public async Task ResolvePath_rejects_existing_symbolic_link_component_when_supported()
    {
        var directories = CreateDirectories();
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var outside = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outside);
        var link = Path.Combine(directories.BooksDirectoryPath, "linked");
        DirectoryLinkTestHelper.CreateDirectoryLink(link, outside);

        var resolver = new AppStoragePathResolver(directories);
        Assert.Throws<InvalidDataException>(() => resolver.ResolvePath("Books/linked/content.txt"));
    }

    [DirectoryLinkTheory]
    [InlineData("Books")]
    [InlineData("Cache")]
    public void ResolvePath_rejects_managed_directory_links_that_escape_the_data_root(string directoryName)
    {
        var directories = CreateDirectories();
        Directory.CreateDirectory(directories.RootDirectoryPath);
        var outside = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outside);
        var link = Path.Combine(directories.RootDirectoryPath, directoryName);
        DirectoryLinkTestHelper.CreateDirectoryLink(link, outside);
        var resolver = new AppStoragePathResolver(directories);

        Assert.Throws<InvalidDataException>(() => resolver.ResolvePath(Path.Combine(directoryName, "payload.bin")));
    }

    private static AppDataDirectoryProvider CreateDirectories() =>
        new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
}
