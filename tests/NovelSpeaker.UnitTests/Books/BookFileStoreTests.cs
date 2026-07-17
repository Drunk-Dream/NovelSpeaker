using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.InfrastructureTests.Books.Import;

public sealed class BookFileStoreTests
{
    [Fact]
    public async Task StageNormalizedTextAsync_and_finalizeAsync_create_content_txt_inside_book_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);

        var store = new BookFileStore(directories);
        var handle = await store.StageNormalizedTextAsync("测试正文", "book-1", progress: null, CancellationToken.None);
        await store.FinalizeAsync(handle, CancellationToken.None);

        Assert.True(File.Exists(handle.FinalPath));
        Assert.False(File.Exists(handle.TemporaryPath));
        Assert.Equal(Path.Combine(directories.BooksDirectoryPath, "book-1", "content.txt"), handle.FinalPath);
        Assert.Equal("测试正文", await File.ReadAllTextAsync(handle.FinalPath, CancellationToken.None));
    }
}
