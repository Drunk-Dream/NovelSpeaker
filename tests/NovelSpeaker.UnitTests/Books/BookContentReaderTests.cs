using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookContentReaderTests
{
    [Fact]
    public async Task ReadChapterTextAsync_returns_requested_slice()
    {
        var path = await CreateContentFileAsync("第一章第二章正文");
        IBookContentReader reader = new BookContentReader();

        var text = await reader.ReadChapterTextAsync(path, 3, 5, CancellationToken.None);

        Assert.Equal("第二章正文", text);
    }

    [Fact]
    public async Task ReadChapterTextAsync_throws_when_start_offset_exceeds_text_length()
    {
        var path = await CreateContentFileAsync("正文");
        IBookContentReader reader = new BookContentReader();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadChapterTextAsync(path, 3, 1, CancellationToken.None));
        Assert.Contains("超出正文长度", exception.Message);
    }

    [Fact]
    public async Task ReadChapterTextAsync_throws_when_length_exceeds_text_length()
    {
        var path = await CreateContentFileAsync("正文");
        IBookContentReader reader = new BookContentReader();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadChapterTextAsync(path, 1, 2, CancellationToken.None));
        Assert.Contains("超出正文长度", exception.Message);
    }

    [Fact]
    public async Task ReadChapterTextAsync_throws_when_file_is_missing()
    {
        IBookContentReader reader = new BookContentReader();
        var path = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.txt");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => reader.ReadChapterTextAsync(path, 0, 1, CancellationToken.None));
    }

    [Fact]
    public async Task ReadChapterTextAsync_reuses_cached_text_for_same_book_file()
    {
        var path = await CreateContentFileAsync("第一章第二章");
        IBookContentReader reader = new BookContentReader();

        var firstRead = await reader.ReadChapterTextAsync(path, 0, 3, CancellationToken.None);
        await File.WriteAllTextAsync(path, "已被替换", CancellationToken.None);
        var secondRead = await reader.ReadChapterTextAsync(path, 3, 3, CancellationToken.None);

        Assert.Equal("第一章", firstRead);
        Assert.Equal("第二章", secondRead);
    }

    private static async Task<string> CreateContentFileAsync(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "content.txt");
        await File.WriteAllTextAsync(path, content, CancellationToken.None);
        return path;
    }
}
