using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.FileSystem.Cache;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Playback;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class SqliteAudioCacheTests
{
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
        AudioCacheProtectionRegistry? registry = null)
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
        var index = new SqliteAudioCacheIndex(factory);
        var fileStore = new AudioCacheFileStore(directories, pathResolver, registry);
        var maintenance = new AudioCacheMaintenance(index, fileStore, limitProvider, registry);
        var cache = new AudioCacheFacade(index, fileStore, maintenance, registry);
        return new CacheFixture(directories, factory, cache, limitProvider);
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
        AudioCacheFacade Cache,
        MutableAudioCacheLimitProvider LimitProvider);

    private sealed class MutableAudioCacheLimitProvider : IAudioCacheLimitProvider
    {
        public long CurrentLimitBytes { get; set; } = AppSettings.DefaultCacheLimitBytes;

        public long GetCurrentLimitBytes() => CurrentLimitBytes;
    }
}
