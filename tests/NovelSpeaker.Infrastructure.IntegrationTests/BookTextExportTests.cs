using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class BookTextExportTests
{
    [Fact]
    public async Task Complete_text_export_uses_safe_names_and_never_overwrites_existing_files()
    {
        using var root = new TemporaryDirectory();
        var directories = new AppDataDirectoryProvider(root.Path);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var resolver = new AppStoragePathResolver(directories);
        var source = Path.Combine(directories.BooksDirectoryPath, "content.txt");
        const string text = "书名\n作者\nChapter\n完整正文\n";
        await File.WriteAllTextAsync(source, text);
        var destination = Path.Combine(root.Path, "export");
        Directory.CreateDirectory(destination);
        var metadata = new ExportMetadata();
        var exporter = new BookTextExportService(metadata, new ExportContent(source), new ExportFileNameSanitizer());
        await File.WriteAllTextAsync(Path.Combine(destination, "_CON.txt"), "existing");
        Assert.True(await exporter.ExportAsync("a", destination, CancellationToken.None));
        Assert.True(await exporter.ExportAsync("b", destination, CancellationToken.None));
        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(destination, "_CON.txt")));
        Assert.Equal(text, await File.ReadAllTextAsync(Path.Combine(destination, "_CON (1).txt")));
        Assert.Equal(text, await File.ReadAllTextAsync(Path.Combine(destination, "_CON (2).txt")));
        Assert.Equal(3, Directory.GetFiles(destination).Length);
        Assert.Equal(text, await File.ReadAllTextAsync(source));
        File.Delete(source);
        Assert.False(await exporter.ExportAsync("a", destination, CancellationToken.None));
    }

    private sealed class ExportContent(string path) : ISourceContentReader
    {
        public Task<string> ReadBookTextAsync(string bookId, ActiveSourceContext expectedContext, CancellationToken cancellationToken) =>
            File.ReadAllTextAsync(path, cancellationToken);
        public Task<string> ReadChapterTextAsync(PlaybackChapterMetadata chapter, CancellationToken cancellationToken) =>
            File.ReadAllTextAsync(path, cancellationToken);
    }

    private sealed class ExportMetadata : IBookPlaybackMetadataQuery
    {
        public Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackBookMetadata?>(new(bookId, "CON", null, [new(0, "Chapter")], new ActiveSourceContext("source", "chapter")));
        public Task<PlaybackChapterMetadata?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackChapterMetadata?>(new(bookId, 0, "Chapter", "source", "chapter", new ActiveSourceContext("source", "chapter")));
    }
}
