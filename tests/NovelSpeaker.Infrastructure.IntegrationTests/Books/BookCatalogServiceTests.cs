using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookLibraryQueryTests
{
    [Fact]
    public async Task GetBooksAsync_prefers_recent_progress_chapter_title_and_exposes_last_played_at()
    {
        var (factory, service) = await CreateCatalogAsync();
        await SeedBookAsync(factory, "book-1", "第一章", "第二章");
        await SeedReadingProgressAsync(factory, "book-1", 1, 3, "2026-06-25T09:00:00.0000000Z");

        var books = await service.GetBooksAsync(CancellationToken.None);
        var book = Assert.Single(books);

        Assert.Equal("第二章", book.CurrentChapterTitle);
        Assert.Equal(DateTimeOffset.Parse("2026-06-25T09:00:00.0000000Z"), book.LastPlayedAt);
        Assert.Equal(2, book.TotalChapterCount);
        Assert.Equal(1, book.CurrentChapterIndex);
        Assert.Equal(0, book.RemainingChapterCount);
        Assert.Equal(1d, book.OverallProgress);
        Assert.True(book.HasReadingProgress);
    }

    [Fact]
    public async Task GetBooksAsync_orders_by_import_time_then_id()
    {
        var (factory, service) = await CreateCatalogAsync();
        await SeedBookAsync(factory, "book-b", "B 第一章", "B 第二章");
        await SeedBookAsync(factory, "book-a", "A 第一章", "A 第二章");
        await SetImportedAtAsync(factory, "book-b", "2026-01-01T00:00:00.0000000Z");
        await SetImportedAtAsync(factory, "book-a", "2026-01-01T00:00:00.0000000Z");

        var books = await service.GetBooksAsync(CancellationToken.None);

        Assert.Equal(["book-a", "book-b"], books.Select(static book => book.Id));
    }

    [Fact]
    public async Task GetBookDetailsAsync_orders_chapters_by_sort_order_then_chapter_index()
    {
        var (factory, service) = await CreateCatalogAsync();
        var repository = new BookImportRepository(factory);
        var now = DateTimeOffset.UtcNow;
        await repository.SaveAsync(
            new Book("book-1", "书籍", null, "book.txt", "book.txt", "hash", "utf-8", now, now, null, now),
            [
                new Chapter("chapter-2", "book-1", 2, 20, "末章", 20, 3),
                new Chapter("chapter-1", "book-1", 1, 10, "中章", 10, 3),
                new Chapter("chapter-0", "book-1", 0, 10, "首章", 0, 3)
            ],
            CancellationToken.None);

        var details = await service.GetBookDetailsAsync("book-1", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal([0, 1, 2], details.Chapters.Select(static chapter => chapter.ChapterIndex));
    }

    [Fact]
    public async Task GetBooksAsync_accepts_legacy_times_and_skips_rows_with_damaged_times()
    {
        var (factory, service) = await CreateCatalogAsync();
        await SeedBookAsync(factory, "legacy-time", "第一章", "第二章");
        await SeedBookAsync(factory, "damaged-time", "第一章", "第二章");
        await SetImportedAtAsync(factory, "legacy-time", "2026-07-16 09:08:07");
        await SetImportedAtAsync(factory, "damaged-time", "not-a-date");

        var books = await service.GetBooksAsync(CancellationToken.None);

        var legacy = Assert.Single(books);
        Assert.Equal("legacy-time", legacy.Id);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 16, 9, 8, 7, TimeSpan.Zero),
            legacy.ImportedAt);
    }

    private static async Task<(SqliteConnectionFactory Factory, BookLibraryQuery Service)> CreateCatalogAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);
        return (factory, new BookLibraryQuery(factory));
    }

    private static async Task SeedBookAsync(SqliteConnectionFactory factory, string bookId, string firstChapterTitle, string secondChapterTitle)
    {
        var repository = new BookImportRepository(factory);
        var now = DateTimeOffset.UtcNow;
        await repository.SaveAsync(
            new Book(bookId, $"书籍 {bookId}", null, $"{bookId}.txt", $"{bookId}.txt", $"{bookId}-hash", "utf-8", now, now, null, now),
            [
                new Chapter($"{bookId}-chapter-1", bookId, 0, 0, firstChapterTitle, 0, 3),
                new Chapter($"{bookId}-chapter-2", bookId, 1, 1, secondChapterTitle, 4, 3)
            ],
            CancellationToken.None);
    }

    private static async Task SeedReadingProgressAsync(SqliteConnectionFactory factory, string bookId, int chapterIndex, int segmentIndex, string updatedAt)
    {
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ReadingProgress (BookId, ChapterIndex, SegmentIndex, CharacterOffset, AudioPositionMilliseconds, UpdatedAt)
            VALUES ($bookId, $chapterIndex, $segmentIndex, $characterOffset, $audioPositionMilliseconds, $updatedAt);

            UPDATE Books
            SET LastPlayedAt = $updatedAt
            WHERE Id = $bookId;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);
        command.Parameters.AddWithValue("$chapterIndex", chapterIndex);
        command.Parameters.AddWithValue("$segmentIndex", segmentIndex);
        command.Parameters.AddWithValue("$characterOffset", 6);
        command.Parameters.AddWithValue("$audioPositionMilliseconds", 200);
        command.Parameters.AddWithValue("$updatedAt", updatedAt);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task SetImportedAtAsync(SqliteConnectionFactory factory, string bookId, string importedAt)
    {
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE Books SET ImportedAt = $importedAt WHERE Id = $bookId;";
        command.Parameters.AddWithValue("$bookId", bookId);
        command.Parameters.AddWithValue("$importedAt", importedAt);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
