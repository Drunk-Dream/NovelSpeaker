using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Persistence;

public sealed class SqliteReadingProgressStoreTests
{
    [Fact]
    public async Task SaveAsync_persists_and_overwrites_progress()
    {
        var (factory, store) = await CreateStoreWithBookAsync("book-1");

        await store.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 120), CancellationToken.None);
        await store.SaveAsync(new PlaybackProgressUpdate("book-1", 1, 2, 30, 450), CancellationToken.None);

        var progress = await store.GetAsync("book-1", CancellationToken.None);

        Assert.NotNull(progress);
        Assert.Equal(1, progress!.ChapterIndex);
        Assert.Equal(2, progress.SegmentIndex);
        Assert.Equal(30, progress.CharacterOffset);
        Assert.Equal(450, progress.AudioPositionMilliseconds);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ReadingProgress WHERE BookId = 'book-1';";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAsync_updates_books_last_played_and_most_recent_progress()
    {
        var (factory, store) = await CreateStoreWithBookAsync("book-1", "book-2");

        await store.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 120), CancellationToken.None);
        await Task.Delay(20);
        await store.SaveAsync(new PlaybackProgressUpdate("book-2", 0, 1, 6, 240), CancellationToken.None);

        var mostRecent = await store.GetMostRecentAsync(CancellationToken.None);
        Assert.NotNull(mostRecent);
        Assert.Equal("book-2", mostRecent!.BookId);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT LastPlayedAt FROM Books WHERE Id = 'book-2';";
        var lastPlayedAt = await command.ExecuteScalarAsync(CancellationToken.None);
        Assert.NotNull(lastPlayedAt);
        Assert.False(string.IsNullOrWhiteSpace(lastPlayedAt?.ToString()));
    }

    private static async Task<(SqliteConnectionFactory Factory, SqliteReadingProgressStore Store)> CreateStoreWithBookAsync(params string[] bookIds)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);

        var bookRepository = new BookImportRepository(factory);
        foreach (var bookId in bookIds)
        {
            var now = DateTimeOffset.UtcNow;
            await bookRepository.SaveAsync(
                new Book(bookId, $"书籍 {bookId}", null, $"{bookId}.txt", $"{bookId}.txt", $"{bookId}-hash", "utf-8", now, now, null, now),
                [new Chapter($"{bookId}-chapter-1", bookId, 0, 0, "第一章", 0, 7)],
                CancellationToken.None);
        }

        return (factory, new SqliteReadingProgressStore(factory));
    }
}
