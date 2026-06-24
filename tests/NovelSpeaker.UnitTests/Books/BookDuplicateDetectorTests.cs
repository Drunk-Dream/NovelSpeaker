using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookDuplicateDetectorTests
{
    [Fact]
    public async Task FindExistingBookIdAsync_returns_existing_id_for_matching_hash()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var seederRepository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(seederRepository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);

        var repository = new BookImportRepository(factory);
        var detector = new BookDuplicateDetector(factory);
        var now = DateTime.UtcNow.ToString("O");
        var book = new Book("book-1", "书名", null, "demo.txt", "stored.txt", "hash-dup", "utf-8", now, now, null, now);
        Chapter[] chapters = [new("c-1", "book-1", 0, 0, "第一章", "正文甲", 0, 3)];

        await repository.SaveAsync(book, chapters, CancellationToken.None);

        var existingId = await detector.FindExistingBookIdAsync("hash-dup", CancellationToken.None);
        Assert.Equal("book-1", existingId);
    }
}
