using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.IntegrationTests.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Persistence;

public sealed class SqliteReadingProgressStoreTests
{
    [Fact]
    public async Task Restart_keeps_book_coordinates_and_obsolete_source_cannot_checkpoint()
    {
        var (factory, store) = await CreateStoreWithBookAsync(null, "book-1");
        var old = await new SqliteBookPlaybackMetadataQuery(factory).GetBookAsync("book-1", CancellationToken.None);
        await store.SaveAsync(new PlaybackProgressUpdate("book-1", 3, 4, 50, 600), CancellationToken.None);
        await SourceBookFixture.SaveAsync(factory, "book-1", ["更新章"]);
        var restarted = new SqliteReadingProgressStore(factory);
        await restarted.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 0, old!.SourceContext), CancellationToken.None);
        var progress = await restarted.GetAsync("book-1", CancellationToken.None);
        Assert.Equal(0, progress!.ChapterIndex);
        Assert.Equal(-1, progress.SegmentIndex);
        Assert.Equal(2, progress.CharacterOffset);
        Assert.Equal(0, progress.AudioPositionMilliseconds);
    }

    [Fact]
    public async Task SaveAsync_persists_and_overwrites_progress()
    {
        var (factory, store) = await CreateStoreWithBookAsync(null, "book-1");

        await store.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 120), CancellationToken.None);
        await store.SaveAsync(new PlaybackProgressUpdate("book-1", 1, 2, 30, 450), CancellationToken.None);

        var progress = await store.GetAsync("book-1", CancellationToken.None);

        Assert.NotNull(progress);
        Assert.Equal(1, progress!.ChapterIndex);
        Assert.Equal(2, progress.SegmentIndex);
        Assert.Equal(30, progress.CharacterOffset);
        Assert.Equal(450, progress.AudioPositionMilliseconds);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ReadingProgress WHERE BookId = 'book-1';";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAsync_updates_books_last_played_and_most_recent_progress()
    {
        var timeProvider = new ManualTimeProvider();
        var (factory, store) = await CreateStoreWithBookAsync(timeProvider, "book-1", "book-2");

        await store.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 120), CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await store.SaveAsync(new PlaybackProgressUpdate("book-2", 0, 1, 6, 240), CancellationToken.None);

        var mostRecent = await store.GetMostRecentAsync(CancellationToken.None);
        Assert.NotNull(mostRecent);
        Assert.Equal("book-2", mostRecent!.BookId);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT LastPlayedAt FROM Books WHERE Id = 'book-2';";
        var lastPlayedAt = await command.ExecuteScalarAsync(CancellationToken.None);
        Assert.Equal("2026-06-26T00:00:01.0000000+00:00", lastPlayedAt);
    }

    [Fact]
    public async Task GetAsync_accepts_legacy_time_and_returns_null_for_damaged_time()
    {
        var (factory, store) = await CreateStoreWithBookAsync(null, "book-1", "book-2");
        await using (var connection = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO ReadingProgress
                    (BookId, ChapterIndex, SegmentIndex, CharacterOffset, AudioPositionMilliseconds, UpdatedAt)
                VALUES
                    ('book-1', 0, 1, 2, 3, '2026-07-16 09:08:07'),
                    ('book-2', 0, 1, 2, 3, 'not-a-date');
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var legacy = await store.GetAsync("book-1", CancellationToken.None);
        var damaged = await store.GetAsync("book-2", CancellationToken.None);

        Assert.NotNull(legacy);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 16, 9, 8, 7, TimeSpan.Zero),
            legacy!.UpdatedAt);
        Assert.Null(damaged);
    }

    private static async Task<(SqliteConnectionFactory Factory, SqliteReadingProgressStore Store)> CreateStoreWithBookAsync(
        TimeProvider? timeProvider = null,
        params string[] bookIds)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);

        foreach (var bookId in bookIds)
        {
            await SourceBookFixture.SaveAsync(factory, bookId, ["第一章"]);
        }

        return (factory, new SqliteReadingProgressStore(factory, timeProvider));
    }
}
