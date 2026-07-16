using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookCatalogServiceTests
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

    private static async Task<(SqliteConnectionFactory Factory, BookCatalogService Service)> CreateCatalogAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);
        return (factory, new BookCatalogService(factory));
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
}
