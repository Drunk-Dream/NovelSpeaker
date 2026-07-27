using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.FileSystem.Cache;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Playback;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class SqliteAudioCacheTests
{
    [Fact]
    public async Task GetValidEntriesAsync_counts_only_decodable_files_and_does_not_touch_lru()
    {
        var timeProvider = new ManualTimeProvider();
        var fixture = await CreateFixtureAsync(timeProvider: timeProvider);
        var validKey = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "当前文本甲");
        var corruptKey = AudioCacheKey.FromPlayback("book-1", 0, 1, 7, 12, "当前文本乙");
        var missingKey = AudioCacheKey.FromPlayback("book-1", 0, 2, 7, 12, "当前文本丙");
        var invalidPathKey = AudioCacheKey.FromPlayback("book-1", 0, 3, 7, 12, "当前文本丁");
        var valid = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                validKey,
                "book-1",
                0,
                0,
                7,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        var corrupt = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                corruptKey,
                "book-1",
                0,
                1,
                7,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        File.Copy(PlaybackTestAudio.CorruptMp3Path, corrupt.FilePath, overwrite: true);
        await InsertIndexedEntryAsync(
            fixture,
            invalidPathKey,
            Path.Combine(Path.GetDirectoryName(fixture.Directories.RootDirectoryPath)!, "outside.mp3"));
        var lastAccessedBefore = await ReadLastAccessedAtAsync(fixture, validKey);
        timeProvider.Advance(TimeSpan.FromHours(1));

        var result = await fixture.Cache.GetValidEntriesAsync(
            [validKey, corruptKey, missingKey, invalidPathKey],
            CancellationToken.None);

        Assert.Equal([validKey], result);
        Assert.Equal(lastAccessedBefore, await ReadLastAccessedAtAsync(fixture, validKey));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Cache.GetValidEntriesAsync([validKey], cancellation.Token));
    }

    [Fact]
    public async Task GetValidEntriesAsync_reads_all_requested_index_entries_through_one_connection()
    {
        var fixture = await CreateFixtureAsync();
        var firstKey = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一段");
        var secondKey = AudioCacheKey.FromPlayback("book-1", 0, 1, 7, 12, "第二段");
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                firstKey,
                "book-1",
                0,
                0,
                7,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                secondKey,
                "book-1",
                0,
                1,
                7,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        fixture.CacheConnectionFactory.Reset();

        var result = await fixture.Cache.GetValidEntriesAsync(
            [firstKey, secondKey],
            CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, fixture.CacheConnectionFactory.OpenCount);
    }

    [Fact]
    public async Task GetValidEntriesAsync_does_not_block_cleanup_and_reads_the_committed_snapshot()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一段");
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
                0,
                0,
                7,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        fixture.CacheConnectionFactory.BlockNextOpen();

        var validityTask = fixture.Cache.GetValidEntriesAsync([key], CancellationToken.None);
        await fixture.CacheConnectionFactory.BlockedOpenStarted.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var cleanup = await fixture.Cache
                .ClearChapterAsync("book-1", 0, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, cleanup.DeletedEntryCount);
        }
        finally
        {
            fixture.CacheConnectionFactory.ReleaseBlockedOpen();
        }

        Assert.Empty(await validityTask);
    }

    [Fact]
    public async Task Changed_is_published_after_successful_mutations_and_not_for_no_ops()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 2, 0, 1, 10, "第一段");
        var changes = new List<CacheChangedEventArgs>();
        fixture.Cache.Changed += (_, eventArgs) => changes.Add(eventArgs);

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
                2,
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);

        Assert.Equal([new CacheChangedEventArgs("book-1", 2)], changes);
        Assert.Equal(1, (await fixture.Cache.GetSummaryAsync(CancellationToken.None)).EntryCount);

        changes.Clear();
        await fixture.Cache.ClearChapterAsync("book-1", 9, CancellationToken.None);
        Assert.Empty(changes);

        await fixture.Cache.InvalidateAsync(key, CancellationToken.None);
        Assert.Equal([new CacheChangedEventArgs(null, null)], changes);
        Assert.Equal(0, (await fixture.Cache.GetSummaryAsync(CancellationToken.None)).EntryCount);
    }

    [Fact]
    public async Task StoreAsync_persists_audio_under_sharded_path_and_try_get_hits()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        var sourceFile = CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path);

        var stored = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key, "book-1", 0, 0, 1, sourceFile, "audio/mpeg"),
            CancellationToken.None);

        Assert.True(File.Exists(stored.FilePath));
        Assert.Contains(Path.Combine("Cache", "Tts", "v1", key.Shard), stored.FilePath);
        Assert.EndsWith($@"{key.FileNameBase}.mp3", stored.FilePath, StringComparison.OrdinalIgnoreCase);

        var hit = await fixture.Cache.TryGetAsync(key, CancellationToken.None);
        Assert.NotNull(hit);
        Assert.Equal(stored.FilePath, hit!.FilePath);
    }

    [Fact]
    public async Task TryGetAsync_removes_stale_database_entry_when_file_is_missing()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        var stored = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key, "book-1", 0, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);

        File.Delete(stored.FilePath);

        var hit = await fixture.Cache.TryGetAsync(key, CancellationToken.None);
        var summary = await fixture.Cache.GetSummaryAsync(CancellationToken.None);

        Assert.Null(hit);
        Assert.Equal(0, summary.EntryCount);
    }

    [Fact]
    public async Task InvalidateAsync_removes_file_and_index()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        var stored = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key, "book-1", 0, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);

        await fixture.Cache.InvalidateAsync(key, CancellationToken.None);

        Assert.False(File.Exists(stored.FilePath));
        Assert.Null(await fixture.Cache.TryGetAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task GetBooksAndChaptersAsync_and_clear_operations_follow_book_and_chapter_boundaries()
    {
        var fixture = await CreateFixtureAsync();
        var key1 = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        var key2 = AudioCacheKey.FromPlayback("book-1", 1, 0, 1, 10, "第二章");
        var key4 = AudioCacheKey.FromPlayback("book-1", 1, 1, 1, 10, "第二章第二段");
        var key3 = AudioCacheKey.FromPlayback("book-2", 0, 0, 1, 10, "其他书");

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key1, "book-1", 0, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key2, "book-1", 1, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key4, "book-1", 1, 1, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key3, "book-2", 0, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoWavPath), "audio/wav"),
            CancellationToken.None);

        var books = await fixture.Cache.GetBooksAsync(CancellationToken.None);
        var book1 = Assert.Single(books, item => item.BookId == "book-1");
        Assert.Equal(2, books.Count);
        Assert.Equal(2, book1.ChapterCount);
        Assert.Equal(3, book1.EntryCount);

        var chapters = await fixture.Cache.GetChaptersAsync("book-1", CancellationToken.None);
        Assert.Equal([0, 1], chapters.Select(item => item.ChapterIndex).ToArray());
        Assert.Equal(2, Assert.Single(chapters, item => item.ChapterIndex == 1).DistinctSegmentCount);

        var chapterCleanup = await fixture.Cache.ClearChapterAsync("book-1", 0, CancellationToken.None);
        Assert.Null(await fixture.Cache.TryGetAsync(key1, CancellationToken.None));
        Assert.NotNull(await fixture.Cache.TryGetAsync(key2, CancellationToken.None));
        Assert.Equal(1, chapterCleanup.DeletedEntryCount);

        var bookCleanup = await fixture.Cache.ClearBookAsync("book-2", CancellationToken.None);
        Assert.Null(await fixture.Cache.TryGetAsync(key3, CancellationToken.None));
        Assert.Equal(1, bookCleanup.DeletedEntryCount);
    }

    [Fact]
    public async Task ClearChaptersAsync_aggregates_partial_results_and_honors_cancellation_before_mutation()
    {
        var registry = new AudioCacheProtectionRegistry();
        var fixture = await CreateFixtureAsync(registry: registry);
        var firstKey = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        var protectedKey = AudioCacheKey.FromPlayback("book-1", 1, 0, 1, 10, "第二段");
        var untouchedKey = AudioCacheKey.FromPlayback("book-1", 2, 0, 1, 10, "第三段");
        var first = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(firstKey, "book-1", 0, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        var protectedEntry = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(protectedKey, "book-1", 1, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(untouchedKey, "book-1", 2, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);

        using (registry.Protect(protectedEntry.FilePath))
        {
            var result = await fixture.Cache.ClearChaptersAsync(
                "book-1",
                [0, 1],
                CancellationToken.None);

            Assert.Equal(1, result.DeletedEntryCount);
            Assert.Equal(1, result.ProtectedEntryCount);
            Assert.False(File.Exists(first.FilePath));
            Assert.NotNull(await fixture.Cache.TryGetAsync(protectedKey, CancellationToken.None));
            Assert.NotNull(await fixture.Cache.TryGetAsync(untouchedKey, CancellationToken.None));
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Cache.ClearChaptersAsync("book-1", [2], cancellation.Token));
        Assert.NotNull(await fixture.Cache.TryGetAsync(untouchedKey, CancellationToken.None));
    }

    [Fact]
    public async Task RunMaintenanceAsync_cleans_tmp_files_and_orphan_cache_files()
    {
        var fixture = await CreateFixtureAsync();
        var shardDirectory = Path.Combine(fixture.Directories.CacheDirectoryPath, "Tts", "v1", "aa");
        Directory.CreateDirectory(shardDirectory);

        var tempFile = Path.Combine(shardDirectory, "leftover.tmp");
        await File.WriteAllTextAsync(tempFile, "tmp", CancellationToken.None);

        var orphanFile = Path.Combine(shardDirectory, "orphan.mp3");
        File.Copy(PlaybackTestAudio.DemoMp3Path, orphanFile, overwrite: true);

        await fixture.Cache.RunMaintenanceAsync(CancellationToken.None);

        Assert.False(File.Exists(tempFile));
        Assert.False(File.Exists(orphanFile));
    }

    [Fact]
    public async Task RunMaintenanceAsync_applies_lru_and_skips_protected_files()
    {
        var registry = new AudioCacheProtectionRegistry();
        var limit = new FileInfo(PlaybackTestAudio.DemoMp3Path).Length + 1;
        var fixture = await CreateFixtureAsync(limit, registry);
        var key1 = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        var key2 = AudioCacheKey.FromPlayback("book-1", 0, 1, 1, 10, "第二段");

        var first = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key1, "book-1", 0, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);

        using var protection = registry.Protect(first.FilePath);

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key2, "book-1", 0, 1, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoWavPath), "audio/wav"),
            CancellationToken.None);
        await fixture.Cache.RunMaintenanceAsync(CancellationToken.None);

        Assert.NotNull(await fixture.Cache.TryGetAsync(key1, CancellationToken.None));
        Assert.Null(await fixture.Cache.TryGetAsync(key2, CancellationToken.None));
    }

    [Fact]
    public async Task GetSummaryAsync_returns_current_runtime_limit()
    {
        var fixture = await CreateFixtureAsync();
        fixture.LimitProvider.CurrentLimitBytes = 512L * 1024 * 1024;

        var summary = await fixture.Cache.GetSummaryAsync(CancellationToken.None);

        Assert.Equal(512L * 1024 * 1024, summary.LimitBytes);
    }

    [Fact]
    public async Task ClearAllAsync_removes_tracked_entries_and_orphans()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key, "book-1", 0, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);

        var orphanDirectory = Path.Combine(fixture.Directories.CacheDirectoryPath, "Tts", "v1", "ff");
        Directory.CreateDirectory(orphanDirectory);
        var orphanFile = Path.Combine(orphanDirectory, "orphan.wav");
        File.Copy(PlaybackTestAudio.DemoWavPath, orphanFile, overwrite: true);

        var cleanup = await fixture.Cache.ClearAllAsync(CancellationToken.None);

        var summary = await fixture.Cache.GetSummaryAsync(CancellationToken.None);
        Assert.Equal(0, summary.EntryCount);
        Assert.False(File.Exists(orphanFile));
        Assert.True(cleanup.DeletedEntryCount > 0);
    }

    [Fact]
    public async Task StoreAsync_serializes_concurrent_writes_without_losing_index_entries()
    {
        var fixture = await CreateFixtureAsync();
        var writes = Enumerable.Range(0, 8)
            .Select(segmentIndex => fixture.Cache.StoreAsync(
                new AudioCacheWriteRequest(
                    AudioCacheKey.FromPlayback("book-1", 0, segmentIndex, 1, 10, $"段落 {segmentIndex}"),
                    "book-1",
                    0,
                    segmentIndex,
                    1,
                    CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                    "audio/mpeg"),
                CancellationToken.None))
            .ToArray();

        await Task.WhenAll(writes);

        var summary = await fixture.Cache.GetSummaryAsync(CancellationToken.None);
        Assert.Equal(writes.Length, summary.EntryCount);
    }

    [Fact]
    public async Task StoreAsync_uses_the_shared_time_provider_and_canonical_sqlite_format()
    {
        var now = new DateTimeOffset(2026, 7, 16, 9, 8, 7, TimeSpan.FromHours(8));
        var fixture = await CreateFixtureAsync(timeProvider: new ManualTimeProvider(now));
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
                0,
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);

        await using var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CreatedAt, LastAccessedAt FROM AudioCacheEntries WHERE CacheKey = $cacheKey;";
        command.Parameters.AddWithValue("$cacheKey", key.Value);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("2026-07-16T01:08:07.0000000+00:00", reader.GetString(0));
        Assert.Equal("2026-07-16T01:08:07.0000000+00:00", reader.GetString(1));
    }

    [Fact]
    public async Task StoreAsync_pre_cancelled_does_not_consume_source_or_leave_cache_files()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        var sourceFile = CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Cache.StoreAsync(
                new AudioCacheWriteRequest(key, "book-1", 0, 0, 1, sourceFile, "audio/mpeg"),
                cancellation.Token));

        Assert.True(File.Exists(sourceFile));
        Assert.False(File.Exists(Path.Combine(
            fixture.Directories.CacheDirectoryPath,
            "Tts",
            AudioCacheKey.CurrentVersion,
            key.Shard,
            $"{key.FileNameBase}.mp3")));
        var ttsDirectory = Path.Combine(fixture.Directories.CacheDirectoryPath, "Tts");
        if (Directory.Exists(ttsDirectory))
        {
            Assert.Empty(Directory.EnumerateFiles(
                ttsDirectory,
                "*.tmp",
                SearchOption.AllDirectories));
        }
        File.Delete(sourceFile);
    }

    [Fact]
    public async Task StoreAsync_index_failure_removes_newly_finalized_cache_file_and_staging()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        await using (var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TRIGGER RejectAudioCacheInsert
                BEFORE INSERT ON AudioCacheEntries
                BEGIN
                    SELECT RAISE(ABORT, 'fixture index failure');
                END;
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await Assert.ThrowsAnyAsync<Exception>(() =>
            fixture.Cache.StoreAsync(
                new AudioCacheWriteRequest(
                    key,
                    "book-1",
                    0,
                    0,
                    1,
                    CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                    "audio/mpeg"),
                CancellationToken.None));

        var shardDirectory = Path.Combine(
            fixture.Directories.CacheDirectoryPath,
            "Tts",
            AudioCacheKey.CurrentVersion,
            key.Shard);
        Assert.Empty(Directory.EnumerateFiles(shardDirectory));
    }

    [Fact]
    public async Task TryGetAsync_rejects_an_index_path_outside_the_application_root()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段");
        var outsidePath = Path.Combine(Path.GetDirectoryName(fixture.Directories.RootDirectoryPath)!, "outside.mp3");

        await using (var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO AudioCacheEntries (
                    CacheKey, BookId, ChapterIndex, SegmentIndex, RuleId, FilePath,
                    ContentType, FileSize, DurationMilliseconds, CreatedAt, LastAccessedAt, Status)
                VALUES ($cacheKey, $bookId, 0, 0, 1, $filePath, 'audio/mpeg', 1, NULL, $now, $now, 1);
                """;
            command.Parameters.AddWithValue("$cacheKey", key.Value);
            command.Parameters.AddWithValue("$bookId", "book-1");
            command.Parameters.AddWithValue("$filePath", outsidePath);
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Cache.TryGetAsync(key, CancellationToken.None));
    }

    private static async Task<CacheFixture> CreateFixtureAsync(
        long? cacheLimitBytes = null,
        AudioCacheProtectionRegistry? registry = null,
        TimeProvider? timeProvider = null)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);

        registry ??= new AudioCacheProtectionRegistry();
        var limitProvider = new MutableAudioCacheLimitProvider
        {
            CurrentLimitBytes = cacheLimitBytes ?? AppSettings.DefaultCacheLimitBytes
        };
        var pathResolver = new AppStoragePathResolver(directories);
        var cacheConnectionFactory = new CountingSqliteConnectionFactory(factory);
        var index = new SqliteAudioCacheIndex(cacheConnectionFactory, timeProvider ?? TimeProvider.System);
        var fileStore = new AudioCacheFileStore(directories, pathResolver, registry);
        var maintenance = new AudioCacheMaintenance(index, fileStore, limitProvider, registry);
        var cache = new AudioCacheFacade(index, fileStore, maintenance, registry, new AudioProbe());
        return new CacheFixture(directories, factory, cacheConnectionFactory, cache, limitProvider);
    }

    private static async Task<string> ReadLastAccessedAtAsync(CacheFixture fixture, AudioCacheKey key)
    {
        await using var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT LastAccessedAt FROM AudioCacheEntries WHERE CacheKey = $cacheKey;";
        command.Parameters.AddWithValue("$cacheKey", key.Value);
        return (string)(await command.ExecuteScalarAsync(CancellationToken.None))!;
    }

    private static async Task InsertIndexedEntryAsync(
        CacheFixture fixture,
        AudioCacheKey key,
        string filePath)
    {
        await using var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AudioCacheEntries (
                CacheKey, BookId, ChapterIndex, SegmentIndex, RuleId, FilePath,
                ContentType, FileSize, DurationMilliseconds, CreatedAt, LastAccessedAt, Status)
            VALUES ($cacheKey, 'book-1', 0, 3, 7, $filePath, 'audio/mpeg', 1, NULL, $now, $now, 1);
            """;
        command.Parameters.AddWithValue("$cacheKey", key.Value);
        command.Parameters.AddWithValue("$filePath", filePath);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static string CopyAudioToTempFile(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}{extension}");
        File.Copy(sourcePath, tempPath, overwrite: true);
        return tempPath;
    }

    private sealed record CacheFixture(
        LocalAppDataDirectoryProvider Directories,
        SqliteConnectionFactory ConnectionFactory,
        CountingSqliteConnectionFactory CacheConnectionFactory,
        AudioCacheFacade Cache,
        MutableAudioCacheLimitProvider LimitProvider);

    private sealed class CountingSqliteConnectionFactory(
        ISqliteConnectionFactory inner) : ISqliteConnectionFactory
    {
        private int _openCount;
        private int _blockNextOpen;
        private TaskCompletionSource _blockedOpenStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _releaseBlockedOpen =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int OpenCount => Volatile.Read(ref _openCount);

        public Task BlockedOpenStarted => _blockedOpenStarted.Task;

        public async Task<Microsoft.Data.Sqlite.SqliteConnection> OpenConnectionAsync(
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _openCount);
            if (Interlocked.Exchange(ref _blockNextOpen, 0) == 1)
            {
                _blockedOpenStarted.TrySetResult();
                await _releaseBlockedOpen.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return await inner.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        public void BlockNextOpen()
        {
            _blockedOpenStarted =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseBlockedOpen =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref _blockNextOpen, 1);
        }

        public void ReleaseBlockedOpen()
        {
            _releaseBlockedOpen.TrySetResult();
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _openCount, 0);
        }
    }

    private sealed class MutableAudioCacheLimitProvider : IAudioCacheLimitProvider
    {
        public long CurrentLimitBytes { get; set; } = AppSettings.DefaultCacheLimitBytes;

        public long GetCurrentLimitBytes() => CurrentLimitBytes;
    }
}
