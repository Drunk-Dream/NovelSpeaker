using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class BookFileStoreTests
{
    [Fact]
    public async Task StageNormalizedTextAsync_and_finalizeAsync_create_content_txt_inside_book_directory()
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
        Assert.Equal("Books/book-1/content.txt", handle.FinalPath);
        Assert.Equal("测试正文", await File.ReadAllTextAsync(finalPath, CancellationToken.None));
    }
}
