using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class BookFileStoreTests
{
    [DirectoryLinkFact]
    public async Task StageNormalizedTextAsync_rejects_a_book_directory_link_before_writing_outside_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var outside = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outside);
        DirectoryLinkTestHelper.CreateDirectoryLink(
            Path.Combine(directories.BooksDirectoryPath, "book-1"),
            outside);
        var store = new BookFileStore(directories, new AppStoragePathResolver(directories));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.StageNormalizedTextAsync("fixture", "book-1", progress: null, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public async Task Staging_a_replacement_keeps_the_previous_snapshot_readable()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);

        var store = new BookFileStore(directories, new AppStoragePathResolver(directories));
        var handle = await store.StageNormalizedTextAsync("测试正文", "book-1", progress: null, CancellationToken.None);
        await store.FinalizeAsync(handle, CancellationToken.None);
        var finalPath = new AppStoragePathResolver(directories).ResolvePath(handle.FinalPath);
        var temporaryPath = new AppStoragePathResolver(directories).ResolvePath(handle.TemporaryPath);

        Assert.True(File.Exists(finalPath));
        Assert.False(File.Exists(temporaryPath));
        Assert.Equal("测试正文", await File.ReadAllTextAsync(finalPath, CancellationToken.None));

        var replacement = await store.StageNormalizedTextAsync("新正文", "book-1", progress: null, CancellationToken.None);
        Assert.NotEqual(handle.FinalPath, replacement.FinalPath);
        await store.FinalizeAsync(replacement, CancellationToken.None);
        Assert.Equal("测试正文", await File.ReadAllTextAsync(finalPath, CancellationToken.None));
        Assert.Equal("新正文", await File.ReadAllTextAsync(new AppStoragePathResolver(directories).ResolvePath(replacement.FinalPath), CancellationToken.None));
    }
}
