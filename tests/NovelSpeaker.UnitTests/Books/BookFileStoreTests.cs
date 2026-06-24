using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookFileStoreTests
{
    [Fact]
    public async Task PrepareCopyAsync_and_finalizeAsync_create_original_txt_inside_book_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);

        var sourceFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(sourceFile, "测试正文");

        var store = new BookFileStore(directories);
        var handle = await store.PrepareCopyAsync(sourceFile, "book-1", progress: null, CancellationToken.None);
        await store.FinalizeAsync(handle, CancellationToken.None);

        Assert.True(File.Exists(handle.FinalPath));
        Assert.False(File.Exists(handle.TemporaryPath));
        Assert.Equal(Path.Combine(directories.BooksDirectoryPath, "book-1", "original.txt"), handle.FinalPath);
    }
}
