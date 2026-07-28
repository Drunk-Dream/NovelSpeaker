using NAudio.Wave;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.FileSystem.Cache;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Playback;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Playback.Export;
using NovelSpeaker.Infrastructure.Speech.Http;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class ChapterMp3ExportWriterTests
{
    [Fact]
    public async Task WriteAsync_preserves_segment_order_and_produces_one_decodable_mp3()
    {
        var fixture = await CreateFixtureAsync();
        var firstKey = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一段");
        var secondKey = AudioCacheKey.FromPlayback("book-1", 0, 1, 7, 12, "第二段");
        await StoreAsync(fixture, firstKey, 0, CreateWaveFile(silence: true));
        await StoreAsync(fixture, secondKey, 1, CreateWaveFile(silence: false));
        var exportRoot = CreateExportRoot();
        var writer = CreateWriter(fixture);

        var result = await writer.WriteAsync(
            new ChapterMp3ExportBatch(
                exportRoot,
                "示例书",
                [new ChapterMp3ExportPlan(0, "001_第一章", [firstKey, secondKey])]),
            CancellationToken.None);

        Assert.Equal(ChapterMp3ExportWriteStatus.Succeeded, result.Status);
        var output = Assert.Single(result.Files);
        Assert.Equal(Path.Combine(exportRoot, "示例书", "001_第一章.mp3"), output.FilePath);
        using var reader = new AudioFileReader(output.FilePath);
        Assert.True(reader.TotalTime > TimeSpan.FromMilliseconds(600));
        var samples = ReadAllSamples(reader);
        var quarter = samples.Length / 4;
        var firstAverage = samples.Take(quarter).Average(Math.Abs);
        var lastAverage = samples.TakeLast(quarter).Average(Math.Abs);
        Assert.True(firstAverage < lastAverage * 0.15f, $"{firstAverage} was not quieter than {lastAverage}.");
    }

    [Fact]
    public async Task WriteAsync_normalizes_mixed_mp3_and_wav_segments_into_one_mp3()
    {
        var fixture = await CreateFixtureAsync();
        var firstKey = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一段");
        var secondKey = AudioCacheKey.FromPlayback("book-1", 0, 1, 7, 12, "第二段");
        await StoreAsync(fixture, firstKey, 0, CopyToTemporaryFile(PlaybackTestAudio.DemoMp3Path));
        await StoreAsync(fixture, secondKey, 1, CreateWaveFile(silence: false));

        var result = await CreateWriter(fixture).WriteAsync(
            new ChapterMp3ExportBatch(
                CreateExportRoot(),
                "示例书",
                [new ChapterMp3ExportPlan(0, "001_第一章", [firstKey, secondKey])]),
            CancellationToken.None);

        using var reader = new AudioFileReader(Assert.Single(result.Files).FilePath);
        Assert.True(reader.TotalTime > TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public async Task WriteAsync_exports_different_chapters_as_separate_mp3_files()
    {
        var fixture = await CreateFixtureAsync();
        var firstKey = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一章");
        var secondKey = AudioCacheKey.FromPlayback("book-1", 1, 0, 7, 12, "第二章");
        await StoreAsync(fixture, firstKey, 0, CreateWaveFile(silence: false));
        await StoreAsync(fixture, secondKey, 0, CreateWaveFile(silence: false), chapterIndex: 1);
        var exportRoot = CreateExportRoot();

        var result = await CreateWriter(fixture).WriteAsync(
            new ChapterMp3ExportBatch(
                exportRoot,
                "示例书",
                [
                    new ChapterMp3ExportPlan(0, "001_第一章", [firstKey]),
                    new ChapterMp3ExportPlan(1, "002_第二章", [secondKey])
                ]),
            CancellationToken.None);

        Assert.Equal(
            [
                Path.Combine(exportRoot, "示例书", "001_第一章.mp3"),
                Path.Combine(exportRoot, "示例书", "002_第二章.mp3")
            ],
            result.Files.Select(file => file.FilePath));
        Assert.All(result.Files, file =>
        {
            using var reader = new AudioFileReader(file.FilePath);
            Assert.True(reader.TotalTime > TimeSpan.Zero);
        });
    }

    [Fact]
    public async Task WriteAsync_rejects_incomplete_cache_before_creating_outputs()
    {
        var fixture = await CreateFixtureAsync();
        var validKey = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "有效段");
        var missingKey = AudioCacheKey.FromPlayback("book-1", 1, 0, 7, 12, "缺失段");
        await StoreAsync(fixture, validKey, 0, CreateWaveFile(silence: false));
        var exportRoot = CreateExportRoot();

        var result = await CreateWriter(fixture).WriteAsync(
            new ChapterMp3ExportBatch(
                exportRoot,
                "示例书",
                [
                    new ChapterMp3ExportPlan(0, "001_第一章", [validKey]),
                    new ChapterMp3ExportPlan(1, "002_第二章", [missingKey])
                ]),
            CancellationToken.None);

        Assert.Equal(ChapterMp3ExportWriteStatus.IncompleteCache, result.Status);
        Assert.Equal(1, result.IncompleteChapterIndex);
        Assert.False(Directory.Exists(Path.Combine(exportRoot, "示例书")));
    }

    [Fact]
    public async Task WriteAsync_rejects_corrupt_cached_audio_as_incomplete()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "损坏段");
        var stored = await StoreAsync(fixture, key, 0, CreateWaveFile(silence: false));
        File.Copy(PlaybackTestAudio.CorruptMp3Path, stored.FilePath, overwrite: true);
        var exportRoot = CreateExportRoot();

        var result = await CreateWriter(fixture).WriteAsync(
            new ChapterMp3ExportBatch(
                exportRoot,
                "示例书",
                [new ChapterMp3ExportPlan(0, "001_第一章", [key])]),
            CancellationToken.None);

        Assert.Equal(ChapterMp3ExportWriteStatus.IncompleteCache, result.Status);
        Assert.False(Directory.Exists(Path.Combine(exportRoot, "示例书")));
    }

    [Fact]
    public async Task WriteAsync_uses_numbered_suffix_without_overwriting_existing_file()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一段");
        await StoreAsync(fixture, key, 0, CreateWaveFile(silence: false));
        var exportRoot = CreateExportRoot();
        var bookDirectory = Path.Combine(exportRoot, "示例书");
        Directory.CreateDirectory(bookDirectory);
        var existingPath = Path.Combine(bookDirectory, "001_第一章.mp3");
        var existingBytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(existingPath, existingBytes);

        var result = await CreateWriter(fixture).WriteAsync(
            new ChapterMp3ExportBatch(
                exportRoot,
                "示例书",
                [new ChapterMp3ExportPlan(0, "001_第一章", [key])]),
            CancellationToken.None);

        Assert.Equal(existingBytes, await File.ReadAllBytesAsync(existingPath));
        Assert.Equal(
            Path.Combine(bookDirectory, "001_第一章 (2).mp3"),
            Assert.Single(result.Files).FilePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteAsync_cleans_staging_and_releases_cache_protection_on_cancel_or_failure(
        bool cancel)
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一段");
        await StoreAsync(fixture, key, 0, CreateWaveFile(silence: false));
        var exportRoot = CreateExportRoot();
        using var cancellation = new CancellationTokenSource();
        var encoder = new FailingChapterMp3Encoder(cancel ? cancellation.Cancel : null);
        var writer = CreateWriter(fixture, encoder);
        var operation = writer.WriteAsync(
            new ChapterMp3ExportBatch(
                exportRoot,
                "示例书",
                [new ChapterMp3ExportPlan(0, "001_第一章", [key])]),
            cancellation.Token);

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => operation);
        }

        var bookDirectory = Path.Combine(exportRoot, "示例书");
        Assert.Empty(Directory.Exists(bookDirectory)
            ? Directory.EnumerateFiles(bookDirectory)
            : []);
        var cleanup = await fixture.Cache.ClearChapterAsync("book-1", 0, CancellationToken.None);
        Assert.Equal(1, cleanup.DeletedEntryCount);
        Assert.Equal(0, cleanup.ProtectedEntryCount);
    }

    [Fact]
    public async Task WriteAsync_keeps_sanitized_output_inside_the_selected_root()
    {
        var fixture = await CreateFixtureAsync();
        var key = AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一段");
        await StoreAsync(fixture, key, 0, CreateWaveFile(silence: false));
        var exportRoot = CreateExportRoot();

        var result = await CreateWriter(fixture).WriteAsync(
            new ChapterMp3ExportBatch(
                exportRoot,
                "..",
                [new ChapterMp3ExportPlan(0, @"..\outside", [key])]),
            CancellationToken.None);

        var output = Assert.Single(result.Files).FilePath;
        Assert.StartsWith(
            Path.GetFullPath(exportRoot) + Path.DirectorySeparatorChar,
            output,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            $"{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}",
            output,
            StringComparison.Ordinal);
    }

    private static ChapterMp3ExportWriter CreateWriter(
        CacheFixture fixture,
        IChapterMp3Encoder? encoder = null) =>
        new(
            fixture.Cache,
            new ExportFileNameSanitizer(),
            encoder ?? new MediaFoundationChapterMp3Encoder());

    private static async Task<AudioCacheEntry> StoreAsync(
        CacheFixture fixture,
        AudioCacheKey key,
        int segmentIndex,
        string sourcePath,
        int chapterIndex = 0)
    {
        return await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
                chapterIndex,
                segmentIndex,
                7,
                sourcePath,
                "audio/wav"),
            CancellationToken.None);
    }

    private static string CopyToTemporaryFile(string sourcePath)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Path.GetRandomFileName()}{Path.GetExtension(sourcePath)}");
        File.Copy(sourcePath, path, overwrite: true);
        return path;
    }

    private static string CreateWaveFile(bool silence)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.wav");
        var format = new WaveFormat(44100, 16, 1);
        using var writer = new WaveFileWriter(path, format);
        const int sampleCount = 44100 / 2;
        for (var index = 0; index < sampleCount; index++)
        {
            var sample = silence
                ? 0f
                : 0.5f * MathF.Sin(2 * MathF.PI * 880 * index / format.SampleRate);
            writer.WriteSample(sample);
        }

        return path;
    }

    private static float[] ReadAllSamples(AudioFileReader reader)
    {
        var samples = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return samples.ToArray();
    }

    private static string CreateExportRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<CacheFixture> CreateFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var connectionFactory = new SqliteConnectionFactory(directories);
        var initializer = new StartupDatabaseInitializer(
            directories,
            new SqliteMigrationRunner(connectionFactory),
            new DefaultChapterRuleSeeder(new ChapterRuleRepository(connectionFactory)));
        await initializer.InitializeAsync(CancellationToken.None);

        await using (var connection = await connectionFactory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Books
                    (Id, Title, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, UpdatedAt)
                VALUES
                    ('book-1', '示例书', 'book.txt', 'Books/book-1/content.txt', 'export-fixture', 'utf-8',
                     '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
                INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, StartOffset, Length)
                VALUES
                    ('chapter-1', 'book-1', 0, 0, '第一章', 0, 1),
                    ('chapter-2', 'book-1', 1, 1, '第二章', 0, 1);
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var protection = new AudioCacheProtectionRegistry();
        var pathResolver = new AppStoragePathResolver(directories);
        var index = new SqliteAudioCacheIndex(connectionFactory, TimeProvider.System);
        var fileStore = new AudioCacheFileStore(directories, pathResolver, protection);
        var maintenance = new AudioCacheMaintenance(
            index,
            fileStore,
            new FixedAudioCacheLimitProvider(),
            protection);
        var cache = new AudioCacheFacade(
            index,
            fileStore,
            maintenance,
            protection,
            new AudioProbe());
        return new CacheFixture(cache);
    }

    private sealed class FixedAudioCacheLimitProvider : IAudioCacheLimitProvider
    {
        public long GetCurrentLimitBytes() => AppSettings.DefaultCacheLimitBytes;
    }

    private sealed class FailingChapterMp3Encoder(Action? beforeFailure) : IChapterMp3Encoder
    {
        public async Task EncodeAsync(
            IReadOnlyList<string> sourceFilePaths,
            Stream destination,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
            beforeFailure?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Synthetic encoder failure.");
        }
    }

    private sealed record CacheFixture(AudioCacheFacade Cache);
}
