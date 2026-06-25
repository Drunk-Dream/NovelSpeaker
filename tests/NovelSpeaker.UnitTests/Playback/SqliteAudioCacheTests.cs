using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
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
        var key3 = AudioCacheKey.FromPlayback("book-2", 0, 0, 1, 10, "其他书");

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key1, "book-1", 0, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key2, "book-1", 1, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key3, "book-2", 0, 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoWavPath), "audio/wav"),
            CancellationToken.None);

        var books = await fixture.Cache.GetBooksAsync(CancellationToken.None);
        var book1 = Assert.Single(books, item => item.BookId == "book-1");
        Assert.Equal(2, books.Count);
        Assert.Equal(2, book1.ChapterCount);
        Assert.Equal(2, book1.EntryCount);

        var chapters = await fixture.Cache.GetChaptersAsync("book-1", CancellationToken.None);
        Assert.Equal([0, 1], chapters.Select(item => item.ChapterIndex).ToArray());

        await fixture.Cache.ClearChapterAsync("book-1", 0, CancellationToken.None);
        Assert.Null(await fixture.Cache.TryGetAsync(key1, CancellationToken.None));
        Assert.NotNull(await fixture.Cache.TryGetAsync(key2, CancellationToken.None));

        await fixture.Cache.ClearBookAsync("book-2", CancellationToken.None);
        Assert.Null(await fixture.Cache.TryGetAsync(key3, CancellationToken.None));
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
        var fixture = await CreateFixtureAsync(new AudioCacheOptions(limit), registry);
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

        await fixture.Cache.ClearAllAsync(CancellationToken.None);

        var summary = await fixture.Cache.GetSummaryAsync(CancellationToken.None);
        Assert.Equal(0, summary.EntryCount);
        Assert.False(File.Exists(orphanFile));
    }

    private static async Task<CacheFixture> CreateFixtureAsync(
        AudioCacheOptions? options = null,
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
        var cache = new SqliteAudioCache(factory, directories, options ?? AudioCacheOptions.Default, registry);
        return new CacheFixture(directories, cache);
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
        SqliteAudioCache Cache);
}
