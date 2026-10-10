using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class BookLibraryQueryTests
{
    [Fact]
    public async Task Catalog_replacement_clamps_read_projections_without_rewriting_book_progress()
    {
        var (factory, library, details) = await CreateCatalogAsync();
        await SourceBookFixture.SaveAsync(factory, "book-1", ["一", "二", "三"]);
        await SeedReadingProgressAsync(factory, "book-1", 2, 99, "2026-06-25T09:00:00.0000000Z");
        var oldCatalog = await details.GetCatalogAsync("book-1", CancellationToken.None);
        await SourceBookFixture.SaveAsync(factory, "book-1", ["新一", "新二"]);
        var summary = Assert.Single(await library.GetBooksAsync(CancellationToken.None));
        var position = await details.GetReadingPositionAsync("book-1", CancellationToken.None);
        var catalog = await details.GetCatalogAsync("book-1", CancellationToken.None);
        Assert.Equal(1, summary.CurrentChapterIndex);
        Assert.Equal("新二", summary.CurrentChapterTitle);
        Assert.Equal(1, position!.ChapterIndex);
        Assert.Equal(99, position.SegmentIndex);
        Assert.NotEqual(oldCatalog[0].ChapterId, catalog[0].ChapterId);
        Assert.Equal(summary.SourceContext, catalog[0].SourceContext);
        var persisted = await new SqliteReadingProgressStore(factory).GetAsync("book-1", CancellationToken.None);
        Assert.Equal(2, persisted!.ChapterIndex);
        Assert.Equal(99, persisted.SegmentIndex);
    }

    [Fact]
    public async Task No_active_source_and_empty_catalog_are_unlocated_without_automatic_fallback()
    {
        var (factory, library, details) = await CreateCatalogAsync();
        await SeedBookAsync(factory, "book-1", "一", "二");
        await SeedReadingProgressAsync(factory, "book-1", 1, 0, "2026-06-25T09:00:00.0000000Z");
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Books SET ActiveSourceId = NULL WHERE Id = 'book-1';";
        await command.ExecuteNonQueryAsync();
        var summary = Assert.Single(await library.GetBooksAsync(CancellationToken.None));
        Assert.Null(summary.SourceContext);
        Assert.Null(summary.CurrentChapterIndex);
        Assert.False(summary.HasReadingProgress);
        Assert.Equal("无活动来源", summary.CurrentChapterTitle);
        Assert.Empty(await details.GetCatalogAsync("book-1", CancellationToken.None));
        Assert.Null(await details.GetReadingPositionAsync("book-1", CancellationToken.None));
        Assert.NotNull(await details.GetHeaderAsync("book-1", CancellationToken.None));
        var metadata = await new SqliteBookPlaybackMetadataQuery(factory).GetBookAsync("book-1", CancellationToken.None);
        Assert.Null(metadata!.SourceContext);
        Assert.Empty(metadata.Chapters);

        command.CommandText = "UPDATE Books SET ActiveSourceId = 'local:book-1'; DELETE FROM Chapters WHERE SourceId = 'local:book-1';";
        await command.ExecuteNonQueryAsync();
        Assert.Null(await details.GetReadingPositionAsync("book-1", CancellationToken.None));
        Assert.Empty(await details.GetCatalogAsync("book-1", CancellationToken.None));
        Assert.NotNull(await new SqliteReadingProgressStore(factory).GetAsync("book-1", CancellationToken.None));
    }

    [Fact]
    public async Task GetBooksAsync_accepts_legacy_times_and_skips_rows_with_damaged_times()
    {
        var (factory, service, _) = await CreateCatalogAsync();
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

    [Fact]
    public async Task GetBooksAsync_prefers_recent_progress_chapter_title_and_exposes_last_played_at()
    {
        var (factory, service, _) = await CreateCatalogAsync();
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
    public async Task GetCatalogAsync_orders_chapters_by_sort_order_then_chapter_index()
    {
        var (factory, _, detailsQuery) = await CreateCatalogAsync();
        await SourceBookFixture.SaveAsync(factory, "book-1", ["首章", "中章", "末章"], sortOrders: [10, 10, 20]);

        var details = await detailsQuery.GetCatalogAsync("book-1", CancellationToken.None);

        Assert.Equal([0, 1, 2], details.Select(static chapter => chapter.ChapterIndex));
    }

    private static async Task<(
        SqliteConnectionFactory Factory,
        BookLibraryQuery Service,
        BookDetailsQuery DetailsQuery)> CreateCatalogAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);
        return (factory, new BookLibraryQuery(factory), new BookDetailsQuery(factory));
    }

    private static async Task SeedBookAsync(SqliteConnectionFactory factory, string bookId, string firstChapterTitle, string secondChapterTitle)
    {
        await SourceBookFixture.SaveAsync(factory, bookId, [firstChapterTitle, secondChapterTitle]);
    }

    private static async Task SeedReadingProgressAsync(SqliteConnectionFactory factory, string bookId, int chapterIndex, int segmentIndex, string updatedAt)
    {
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        using var command = connection.CreateCommand();
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
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Books SET ImportedAt = $importedAt WHERE Id = $bookId;";
        command.Parameters.AddWithValue("$bookId", bookId);
        command.Parameters.AddWithValue("$importedAt", importedAt);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
