using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.UnitTests.Playback;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookManagementServiceTests
{
    [Fact]
    public async Task GetBookDetailsAsync_and_UpdateMetadataAsync_return_enriched_summary()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", title: "原书名", author: null);
        await fixture.ProgressStore.SaveAsync(new PlaybackProgressUpdate("book-1", 1, 0, 8, 240), CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                AudioCacheKey.FromPlayback("book-1", 1, 0, 1, 10, "第二章 第一段"),
                "book-1",
                1,
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);

        var details = await fixture.Service.GetBookDetailsAsync("book-1", CancellationToken.None);
        var updated = await fixture.Service.UpdateMetadataAsync(
            new BookMetadataUpdateRequest("book-1", "  新书名  ", "  作者甲  "),
            CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(2, details!.TotalChapterCount);
        Assert.Equal(1, details.CurrentChapterIndex);
        Assert.Equal(0, details.RemainingChapterCount);
        Assert.Equal(1d, details.OverallProgress);
        Assert.True(details.HasReadingProgress);
        Assert.True(details.CachedAudioBytes > 0);
        Assert.Equal("新书名", updated.Title);
        Assert.Equal("作者甲", updated.Author);
    }

    [Fact]
    public async Task DeleteAsync_removes_book_progress_and_internal_files()
    {
        var fixture = await CreateFixtureAsync();
        var storedFilePath = await SeedBookAsync(fixture, "book-1", title: "待删除书籍", author: "作者");
        await fixture.ProgressStore.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 120), CancellationToken.None);

        var result = await fixture.Service.DeleteAsync(new BookDeleteRequest("book-1", false), CancellationToken.None);
        var remainingDetails = await fixture.Service.GetBookDetailsAsync("book-1", CancellationToken.None);
        var remainingProgress = await fixture.ProgressStore.GetAsync("book-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("book-1", result!.BookId);
        Assert.False(result.DeletedAudioCache);
        Assert.Equal(2, result.DeletedChapterCount);
        Assert.True(result.DeletedReadingProgress);
        Assert.Null(remainingDetails);
        Assert.Null(remainingProgress);
        Assert.False(File.Exists(storedFilePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(storedFilePath)!));
    }

    [Fact]
    public async Task DeleteAsync_restores_staged_files_when_database_delete_fails()
    {
        var fixture = await CreateFixtureAsync();
        var storedFilePath = await SeedBookAsync(fixture, "book-1", title: "触发回滚", author: null);

        await using (var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None))
        {
            var trigger = connection.CreateCommand();
            trigger.CommandText =
                """
                CREATE TRIGGER BlockBookDelete
                BEFORE DELETE ON Books
                BEGIN
                    SELECT RAISE(ABORT, 'blocked');
                END;
                """;
            await trigger.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await Assert.ThrowsAsync<SqliteException>(() =>
            fixture.Service.DeleteAsync(new BookDeleteRequest("book-1", false), CancellationToken.None));

        Assert.True(File.Exists(storedFilePath));
        Assert.NotNull(await fixture.Service.GetBookDetailsAsync("book-1", CancellationToken.None));
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);

        var protectionRegistry = new AudioCacheProtectionRegistry();
        var cache = new SqliteAudioCache(factory, directories, AudioCacheOptions.Default, protectionRegistry);
        var progressStore = new SqliteReadingProgressStore(factory);
        var service = new BookManagementService(factory, directories, cache, protectionRegistry);
        return new TestFixture(directories, factory, cache, progressStore, service);
    }

    private static async Task<string> SeedBookAsync(TestFixture fixture, string bookId, string title, string? author)
    {
        var storedDirectory = Path.Combine(fixture.Directories.BooksDirectoryPath, bookId);
        Directory.CreateDirectory(storedDirectory);
        var storedFilePath = Path.Combine(storedDirectory, "content.txt");
        await File.WriteAllTextAsync(storedFilePath, "第一章 第一段第二章 第一段", CancellationToken.None);

        var repository = new BookImportRepository(fixture.Factory);
        var now = DateTime.UtcNow.ToString("O");
        await repository.SaveAsync(
            new Domain.Books.Book(
                bookId,
                title,
                author,
                $"{bookId}.txt",
                storedFilePath,
                $"{bookId}-hash",
                "utf-8",
                now,
                now,
                null,
                now),
            [
                new Domain.Books.Chapter($"{bookId}-chapter-1", bookId, 0, 0, "第一章", 0, 6),
                new Domain.Books.Chapter($"{bookId}-chapter-2", bookId, 1, 1, "第二章", 6, 6)
            ],
            CancellationToken.None);

        return storedFilePath;
    }

    private static string CopyAudioToTempFile(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}{extension}");
        File.Copy(sourcePath, tempPath, overwrite: true);
        return tempPath;
    }

    private sealed record TestFixture(
        LocalAppDataDirectoryProvider Directories,
        SqliteConnectionFactory Factory,
        SqliteAudioCache Cache,
        SqliteReadingProgressStore ProgressStore,
        BookManagementService Service);
}
