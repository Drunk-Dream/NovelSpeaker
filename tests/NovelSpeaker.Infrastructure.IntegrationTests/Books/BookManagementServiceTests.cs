using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.FileSystem.Cache;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.Infrastructure.Persistence.Playback;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.Infrastructure.IntegrationTests;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class BookLibraryPersistenceTests
{
    [Fact]
    public async Task Queries_and_delete_return_null_when_book_is_missing()
    {
        var fixture = await CreateFixtureAsync();

        Assert.Null(await fixture.Query.GetBookDetailsHeaderAsync("missing", CancellationToken.None));
        Assert.Null(await fixture.Query.GetBookDetailsAsync("missing", CancellationToken.None));
        Assert.Null(await fixture.Deletion.DeleteAsync(new BookDeleteRequest("missing", true), CancellationToken.None));
    }

    [Fact]
    public async Task GetBookDetailsAsync_and_UpdateMetadataAsync_return_enriched_summary()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", title: "原书名", author: null);
        await fixture.ProgressStore.SaveAsync(new PlaybackProgressUpdate("book-1", 1, 0, 8, 240), CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                TestAudioCacheKey.Create("book-1", 1, 0, 1, 10, "第二章 第一段"),
                "book-1",
                1,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);

        var header = await fixture.Query.GetBookDetailsHeaderAsync("book-1", CancellationToken.None);
        var details = await fixture.Query.GetBookDetailsAsync("book-1", CancellationToken.None);
        var updated = await fixture.Metadata.UpdateMetadataAsync(
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
            fixture.Metadata.UpdateMetadataAsync(
                new BookMetadataUpdateRequest("book-1", "   ", "作者"),
                CancellationToken.None));

        var updated = await fixture.Metadata.UpdateMetadataAsync(
            new BookMetadataUpdateRequest("book-1", "新书名", "   "),
            CancellationToken.None);

        Assert.Equal("新书名", updated.Title);
        Assert.Null(updated.Author);
    }

    [Fact]
    public async Task UpdateMetadataAsync_does_not_partially_write_when_book_is_missing()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", title: "保留书名", author: "保留作者");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Metadata.UpdateMetadataAsync(
            new BookMetadataUpdateRequest("missing", "新书名", "新作者"),
            CancellationToken.None));

        var unchanged = await fixture.Query.GetBookDetailsHeaderAsync("book-1", CancellationToken.None);
        Assert.NotNull(unchanged);
        Assert.Equal("保留书名", unchanged.Title);
        Assert.Equal("保留作者", unchanged.Author);
    }

    [Fact]
    public async Task DeleteAsync_removes_book_progress_and_internal_files()
    {
        var fixture = await CreateFixtureAsync();
        var storedFilePath = await SeedBookAsync(fixture, "book-1", title: "待删除书籍", author: "作者");
        await fixture.ProgressStore.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 120), CancellationToken.None);

        var result = await fixture.Deletion.DeleteAsync(new BookDeleteRequest("book-1", false), CancellationToken.None);
        var remainingDetails = await fixture.Query.GetBookDetailsAsync("book-1", CancellationToken.None);
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
    public async Task DeleteAsync_removes_all_book_owned_plan_and_cache_rows_before_completion()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", title: "大量朗读计划", author: null);
        await fixture.ProgressStore.SaveAsync(
            new PlaybackProgressUpdate("book-1", 1, 0, 0, 120),
            CancellationToken.None);

        await using (var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO ChapterSpeechPlans
                    (ChapterId, ChapterRevisionHash, TextProfileFingerprint, PlanOutputHash, State, BodySegmentCount, UpdatedAt)
                VALUES
                    ('book-1-chapter-1', zeroblob(32), zeroblob(32), zeroblob(32), 1, 1, $now),
                    ('book-1-chapter-2', zeroblob(32), zeroblob(32), zeroblob(32), 1, 1, $now);
                INSERT INTO ChapterSpeechPlanSegments
                    (ChapterId, OrderIndex, SegmentKind, SourceStartOffset, SourceLength, SpeechTextHash)
                VALUES
                    ('book-1-chapter-1', 0, 0, 0, 1, zeroblob(32)),
                    ('book-1-chapter-2', 0, 0, 0, 1, zeroblob(32));
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段"),
                "book-1",
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);

        await fixture.Deletion.DeleteAsync(
            new BookDeleteRequest("book-1", true),
            CancellationToken.None);

        await using var verifyConnection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None);
        foreach (var table in new[]
                 {
                     "Books",
                     "Chapters",
                     "ReadingProgress",
                     "ChapterSpeechPlans",
                     "ChapterSpeechPlanSegments",
                     "AudioCacheEntries"
                 })
        {
            var command = verifyConnection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {(table is "Books" or "Chapters" ? "Id" : table is "ChapterSpeechPlans" or "ChapterSpeechPlanSegments" ? "ChapterId" : "BookId")} LIKE 'book-1%';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(CancellationToken.None))!);
        }
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
            fixture.Deletion.DeleteAsync(new BookDeleteRequest("book-1", false), CancellationToken.None));

        Assert.True(File.Exists(storedFilePath));
        Assert.NotNull(await fixture.Query.GetBookDetailsAsync("book-1", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_restores_book_and_cache_when_a_cache_file_is_protected()
    {
        var fixture = await CreateFixtureAsync();
        var storedFilePath = await SeedBookAsync(fixture, "book-1", title: "受保护缓存", author: null);
        var cacheEntry = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段"),
                "book-1",
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        using var protection = fixture.ProtectionRegistry.Protect(cacheEntry.FilePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Deletion.DeleteAsync(
            new BookDeleteRequest("book-1", true),
            CancellationToken.None));

        Assert.True(File.Exists(storedFilePath));
        Assert.True(File.Exists(cacheEntry.FilePath));
        Assert.NotNull(await fixture.Cache.TryGetAsync(cacheEntry.Key, CancellationToken.None));
        Assert.NotNull(await fixture.Query.GetBookDetailsAsync("book-1", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_restores_already_staged_cache_when_a_later_cache_file_is_protected()
    {
        var fixture = await CreateFixtureAsync();
        var storedFilePath = await SeedBookAsync(fixture, "book-1", title: "部分缓存暂存", author: null);
        var entries = new List<AudioCacheEntry>();
        foreach (var segment in new[] { (Index: 0, Text: "第一段"), (Index: 1, Text: "第二段") })
        {
            entries.Add(await fixture.Cache.StoreAsync(
                new AudioCacheWriteRequest(
                    TestAudioCacheKey.Create("book-1", 0, segment.Index, 1, 10, segment.Text),
                    "book-1",
                    0,
                    1,
                    CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                    "audio/mpeg"),
                CancellationToken.None));
        }

        var ordered = entries.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal).ToArray();
        using var protection = fixture.ProtectionRegistry.Protect(ordered[1].FilePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Deletion.DeleteAsync(
            new BookDeleteRequest("book-1", true),
            CancellationToken.None));

        Assert.True(File.Exists(storedFilePath));
        Assert.All(entries, entry => Assert.True(File.Exists(entry.FilePath)));
        foreach (var entry in entries)
        {
            Assert.NotNull(await fixture.Cache.TryGetAsync(entry.Key, CancellationToken.None));
        }

        Assert.NotNull(await fixture.Query.GetBookDetailsAsync("book-1", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_rejects_tampered_book_path_and_never_touches_external_file()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", title: "恶意路径", author: null);
        var externalPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.txt");
        await File.WriteAllTextAsync(externalPath, "external source", CancellationToken.None);
        await using (var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Books SET StoredFilePath = $path WHERE Id = 'book-1';";
            command.Parameters.AddWithValue("$path", externalPath);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Deletion.DeleteAsync(new BookDeleteRequest("book-1", false), CancellationToken.None));

        Assert.True(File.Exists(externalPath));
        Assert.Equal("external source", await File.ReadAllTextAsync(externalPath, CancellationToken.None));
        Assert.NotNull(await fixture.Query.GetBookDetailsAsync("book-1", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_rejects_tampered_cache_path_and_never_touches_external_file()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", title: "恶意缓存路径", author: null);
        var cacheEntry = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段"),
                "book-1",
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        var externalPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.mp3");
        File.Copy(cacheEntry.FilePath, externalPath, overwrite: true);

        await using (var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE AudioCacheEntries SET FilePath = $path WHERE CacheKey = $cacheKey;";
            command.Parameters.AddWithValue("$path", externalPath);
            command.Parameters.AddWithValue("$cacheKey", System.Text.Encoding.UTF8.GetBytes(cacheEntry.Key.Value));
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Deletion.DeleteAsync(
                new BookDeleteRequest("book-1", true),
                CancellationToken.None));

            Assert.True(File.Exists(externalPath));
            Assert.NotNull(await fixture.Query.GetBookDetailsAsync("book-1", CancellationToken.None));
        }
        finally
        {
            File.Delete(externalPath);
        }
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
        var pathResolver = new AppStoragePathResolver(directories);
        var index = new SqliteAudioCacheIndex(factory, TimeProvider.System);
        var fileStore = new AudioCacheFileStore(directories, pathResolver, protectionRegistry);
        var limitProvider = new FixedAudioCacheLimitProvider(AppSettings.DefaultCacheLimitBytes);
        var maintenance = new AudioCacheMaintenance(index, fileStore, limitProvider, protectionRegistry);
        var cache = new AudioCacheFacade(index, fileStore, maintenance, protectionRegistry, new AudioProbe());
        var progressStore = new SqliteReadingProgressStore(factory);
        var query = new BookLibraryQuery(factory);
        var metadata = new BookMetadataUpdateService(factory);
        var journal = new SqliteBookOperationJournal(factory, TimeProvider.System);
        var deletionStore = new BookDeletionOperationStore(
            factory,
            directories,
            protectionRegistry,
            pathResolver,
            journal,
            TimeProvider.System);
        var deletion = new Application.Books.Library.BookDeletionService(deletionStore);
        return new TestFixture(directories, factory, cache, progressStore, protectionRegistry, query, metadata, deletion);
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
        AudioCacheFacade Cache,
        SqliteReadingProgressStore ProgressStore,
        AudioCacheProtectionRegistry ProtectionRegistry,
        BookLibraryQuery Query,
        BookMetadataUpdateService Metadata,
        IBookDeletionService Deletion);

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
