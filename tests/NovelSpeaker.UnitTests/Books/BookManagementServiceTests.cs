using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Settings;
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

        var header = await fixture.Service.GetBookDetailsHeaderAsync("book-1", CancellationToken.None);
        var details = await fixture.Service.GetBookDetailsAsync("book-1", CancellationToken.None);
        var updated = await fixture.Service.UpdateMetadataAsync(
            new BookMetadataUpdateRequest("book-1", "  新书名  ", "  作者甲  "),
            CancellationToken.None);

        Assert.NotNull(header);
        Assert.Equal("原书名", header!.Title);
        Assert.Null(header.Author);
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
    public async Task UpdateMetadataAsync_rejects_blank_title_and_normalizes_blank_author()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", title: "原书名", author: "作者");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UpdateMetadataAsync(
                new BookMetadataUpdateRequest("book-1", "   ", "作者"),
                CancellationToken.None));

        var updated = await fixture.Service.UpdateMetadataAsync(
            new BookMetadataUpdateRequest("book-1", "新书名", "   "),
            CancellationToken.None);

        Assert.Equal("新书名", updated.Title);
        Assert.Null(updated.Author);
    }

    [Fact]
    public async Task ClearBookCacheAsync_skips_protected_files_and_returns_actual_cleared_bytes()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", title: "缓存测试", author: null);

        var first = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段"),
                "book-1",
                0,
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        var second = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                AudioCacheKey.FromPlayback("book-1", 0, 1, 1, 10, "第二段"),
                "book-1",
                0,
                1,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        using var protection = fixture.ProtectionRegistry.Protect(second.FilePath);

        var clearedBytes = await fixture.Service.ClearBookCacheAsync("book-1", CancellationToken.None);
        var remainingDetails = await fixture.Service.GetBookDetailsAsync("book-1", CancellationToken.None);

        Assert.True(clearedBytes > 0);
        Assert.False(File.Exists(first.FilePath));
        Assert.True(File.Exists(second.FilePath));
        Assert.NotNull(remainingDetails);
        Assert.True(remainingDetails!.CachedAudioBytes > 0);
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

    [Fact]
    public async Task DeleteAsync_restores_book_and_cache_when_a_cache_file_is_protected()
    {
        var fixture = await CreateFixtureAsync();
        var storedFilePath = await SeedBookAsync(fixture, "book-1", title: "受保护缓存", author: null);
        var cacheEntry = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段"),
                "book-1",
                0,
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        using var protection = fixture.ProtectionRegistry.Protect(cacheEntry.FilePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.DeleteAsync(
            new BookDeleteRequest("book-1", true),
            CancellationToken.None));

        Assert.True(File.Exists(storedFilePath));
        Assert.True(File.Exists(cacheEntry.FilePath));
        Assert.NotNull(await fixture.Cache.TryGetAsync(cacheEntry.Key, CancellationToken.None));
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
        var cache = new SqliteAudioCache(
            factory,
            directories,
            new FixedAudioCacheLimitProvider(AppSettings.DefaultCacheLimitBytes),
            protectionRegistry);
        var progressStore = new SqliteReadingProgressStore(factory);
        var service = new BookManagementService(factory, directories, cache, protectionRegistry);
        return new TestFixture(directories, factory, cache, progressStore, protectionRegistry, service);
    }

    private static async Task<string> SeedBookAsync(TestFixture fixture, string bookId, string title, string? author)
    {
        var storedDirectory = Path.Combine(fixture.Directories.BooksDirectoryPath, bookId);
        Directory.CreateDirectory(storedDirectory);
        var storedFilePath = Path.Combine(storedDirectory, "content.txt");
        await File.WriteAllTextAsync(storedFilePath, "第一章 第一段第二章 第一段", CancellationToken.None);

        var repository = new BookImportRepository(fixture.Factory);
        var now = DateTimeOffset.UtcNow;
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
        AudioCacheProtectionRegistry ProtectionRegistry,
        BookManagementService Service);

    private sealed class FixedAudioCacheLimitProvider : IAudioCacheLimitProvider
    {
        public FixedAudioCacheLimitProvider(long currentLimitBytes)
        {
            CurrentLimitBytes = currentLimitBytes;
        }

        public long CurrentLimitBytes { get; }

        public long GetCurrentLimitBytes() => CurrentLimitBytes;
    }
}
