using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class SourceContentReaderTests
{
    [Fact]
    public async Task ReadChapterTextAsync_resolves_source_owned_slice()
    {
        using var fixture = await Fixture.CreateAsync("第一章第二章正文", 3, 5);
        Assert.Equal("第二章正文", await fixture.Reader.ReadChapterTextAsync("local:book", fixture.ChapterId, CancellationToken.None));
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(1, 2)]
    public async Task ReadChapterTextAsync_rejects_ranges_outside_content(int start, int length)
    {
        using var fixture = await Fixture.CreateAsync("正文", start, length);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Reader.ReadChapterTextAsync("local:book", fixture.ChapterId, CancellationToken.None));
    }

    [Fact]
    public async Task ReadChapterTextAsync_reports_missing_snapshot_file()
    {
        using var fixture = await Fixture.CreateAsync("正文", 0, 2);
        File.Delete(fixture.ContentPath);
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            fixture.Reader.ReadChapterTextAsync("local:book", fixture.ChapterId, CancellationToken.None));
    }

    [Fact]
    public async Task Replacement_reads_new_snapshot_and_rejects_old_technical_chapter()
    {
        using var fixture = await Fixture.CreateAsync("旧正文", 0, 3);
        Assert.Equal("旧正文", await fixture.Reader.ReadChapterTextAsync("local:book", fixture.ChapterId, CancellationToken.None));
        var replacement = Path.Combine(fixture.Root.Path, "new.txt");
        await File.WriteAllTextAsync(replacement, "新正文");
        await SourceBookFixture.SaveAsync(fixture.Factory, "book", ["章"], replacement);
        var chapter = await new SqliteBookPlaybackMetadataQuery(fixture.Factory).GetChapterAsync("book", 0, CancellationToken.None);
        Assert.Equal("新正文", await fixture.Reader.ReadChapterTextAsync("local:book", chapter!.ChapterId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Reader.ReadChapterTextAsync("local:book", fixture.ChapterId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Reader.ReadChapterTextAsync("another-source", chapter.ChapterId, CancellationToken.None));
    }

    private sealed class Fixture : IDisposable
    {
        public TemporaryDirectory Root { get; } = new();
        public SqliteConnectionFactory Factory { get; private set; } = null!;
        public ISourceContentReader Reader { get; private set; } = null!;
        public string ChapterId { get; private set; } = "";
        public string ContentPath { get; private set; } = "";

        public static async Task<Fixture> CreateAsync(string text, int start, int length)
        {
            var fixture = new Fixture();
            var directories = new AppDataDirectoryProvider(fixture.Root.Path);
            fixture.Factory = new SqliteConnectionFactory(directories, null, pooling: false);
            await new StartupDatabaseInitializer(directories, new SqliteMigrationRunner(fixture.Factory),
                new DefaultChapterRuleSeeder(new ChapterRuleRepository(fixture.Factory))).InitializeAsync(CancellationToken.None);
            fixture.ContentPath = Path.Combine(fixture.Root.Path, "content.txt");
            await File.WriteAllTextAsync(fixture.ContentPath, text);
            await SourceBookFixture.SaveAsync(fixture.Factory, "book", ["章"], fixture.ContentPath);
            var chapter = await new SqliteBookPlaybackMetadataQuery(fixture.Factory).GetChapterAsync("book", 0, CancellationToken.None);
            fixture.ChapterId = chapter!.ChapterId;
            await using var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE LocalChapterContents SET StartOffset = $start, Length = $length;";
            command.Parameters.AddWithValue("$start", start);
            command.Parameters.AddWithValue("$length", length);
            await command.ExecuteNonQueryAsync();
            fixture.Reader = new SourceContentReader(new AppStoragePathResolver(directories), fixture.Factory);
            return fixture;
        }

        public void Dispose() => Root.Dispose();
    }
}
