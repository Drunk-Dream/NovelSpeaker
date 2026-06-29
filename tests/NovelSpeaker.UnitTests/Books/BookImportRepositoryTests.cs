using Microsoft.Data.Sqlite;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookImportRepositoryTests
{
    [Fact]
    public async Task SaveAsync_rolls_back_when_a_chapter_insert_breaks_the_unique_index()
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
        var now = DateTime.UtcNow.ToString("O");
        var book = new Book("book-1", "书名", null, "demo.txt", "stored.txt", "hash-1", "utf-8", now, now, null, now);
        Chapter[] chapters =
        [
            new("c-1", "book-1", 0, 0, "第一章", 0, 3),
            new("c-2", "book-1", 0, 10, "第二章", 10, 3)
        ];

        await Assert.ThrowsAsync<SqliteException>(() => repository.SaveAsync(book, chapters, CancellationToken.None));

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Books;";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SaveAsync_persists_last_import_timestamps_and_chapter_sort_order()
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
        var now = DateTime.UtcNow.ToString("O");
        var book = new Book("book-2", "书名", null, "demo.txt", "stored.txt", "hash-2", "utf-8", now, now, null, now);
        Chapter[] chapters =
        [
            new("c-1", "book-2", 0, 25, "第一章", 4, 3)
        ];

        await repository.SaveAsync(book, chapters, CancellationToken.None);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var bookCommand = connection.CreateCommand();
        bookCommand.CommandText = "SELECT LastImportedAt, LastPlayedAt FROM Books WHERE Id = 'book-2';";
        await using var reader = await bookCommand.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(now, reader.GetString(0));
        Assert.True(reader.IsDBNull(1));

        var chapterCommand = connection.CreateCommand();
        chapterCommand.CommandText = "SELECT SortOrder FROM Chapters WHERE BookId = 'book-2';";
        var sortOrder = Convert.ToInt32(await chapterCommand.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(25, sortOrder);
    }
}
