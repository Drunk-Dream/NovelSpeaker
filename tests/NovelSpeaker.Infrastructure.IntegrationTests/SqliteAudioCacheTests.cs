using System.Text;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Books;
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
    public async Task Current_configuration_query_distinguishes_plan_states_empty_content_and_title_coverage()
    {
        var fixture = await CreateFixtureAsync();
        var profile = TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "正文一").Identity.SynthesisProfile;
        var planStore = new SqliteChapterSpeechPlanStore(fixture.ConnectionFactory);
        await planStore.SaveAsync(
            CreatePlan(
                "cache-chapter-1-0",
                ChapterSpeechPlanState.Ready,
                [
                    CreatePlanSegment(0, 0, "正文一"),
                    CreatePlanSegment(1, 1, "正文二")
                ]),
            CancellationToken.None);
        await planStore.SaveAsync(
            CreatePlan("cache-chapter-1-1", ChapterSpeechPlanState.Computing, []),
            CancellationToken.None);
        await planStore.SaveAsync(
            CreatePlan("cache-chapter-1-2", ChapterSpeechPlanState.Failed, []),
            CancellationToken.None);
        await planStore.SaveAsync(
            CreatePlan(
                "cache-chapter-1-3",
                ChapterSpeechPlanState.Ready,
                [CreatePlanSegment(0, 0, "尚未缓存")]),
            CancellationToken.None);

        var firstBodyKey = TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "正文一");
        var secondBodyKey = TestAudioCacheKey.Create("book-1", 0, 1, 7, 12, "正文二");
        var staleProfileKey = TestAudioCacheKey.Create("book-1", 0, 1, 7, 13, "正文二");
        var titleKey = TestAudioCacheKey.CreateTitle("book-1", 0, 7, 12, "第一章");
        await InsertIndexedCoverageEntryAsync(
            fixture,
            firstBodyKey,
            "cache-chapter-1-0",
            profile,
            healthState: 1);
        await InsertIndexedCoverageEntryAsync(
            fixture,
            secondBodyKey,
            "cache-chapter-1-0",
            profile,
            healthState: 2);
        await InsertIndexedCoverageEntryAsync(
            fixture,
            staleProfileKey,
            "cache-chapter-1-0",
            staleProfileKey.Identity.SynthesisProfile,
            healthState: 1);
        await InsertIndexedCoverageEntryAsync(
            fixture,
            titleKey,
            "cache-chapter-1-0",
            profile,
            healthState: 1);

        fixture.CacheConnectionFactory.Reset();
        var lastAccessedBefore = await ReadLastAccessedAtAsync(fixture, firstBodyKey);
        fixture.CacheConnectionFactory.Reset();
        var statuses = await fixture.Cache.GetCurrentConfigurationStatusesAsync(
            [
                new CurrentCacheChapterQuery(
                    "cache-chapter-1-0",
                    0,
                    true,
                    Fingerprint.Sha256("第一章")),
                new CurrentCacheChapterQuery("cache-chapter-1-1", 1, false, null),
                new CurrentCacheChapterQuery("cache-chapter-1-2", 2, false, null),
                new CurrentCacheChapterQuery("cache-chapter-1-3", 3, false, null),
                new CurrentCacheChapterQuery("missing-plan", 4, false, null)
            ],
            profile,
            CancellationToken.None);

        Assert.Equal(1, fixture.CacheConnectionFactory.OpenCount);
        Assert.Equal(new ChapterCacheStatus(0, 2, 3), statuses[0]);
        Assert.Equal(ChapterCacheStatusKind.Available, statuses[0].Kind);
        Assert.Equal(ChapterCacheStatusKind.PlanUnavailable, statuses[1].Kind);
        Assert.Equal(ChapterCacheStatusKind.PlanUnavailable, statuses[2].Kind);
        Assert.Equal(new ChapterCacheStatus(3, 0, 1), statuses[3]);
        Assert.Equal(ChapterCacheStatusKind.Available, statuses[3].Kind);
        Assert.Equal(ChapterCacheStatusKind.PlanMissing, statuses[4].Kind);
        Assert.Equal(lastAccessedBefore, await ReadLastAccessedAtAsync(fixture, firstBodyKey));
    }

    [Fact]
    public async Task Current_configuration_query_treats_ready_empty_plan_as_no_playable_content_and_honors_title_switch()
    {
        var fixture = await CreateFixtureAsync();
        var profile = TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "正文").Identity.SynthesisProfile;
        var planStore = new SqliteChapterSpeechPlanStore(fixture.ConnectionFactory);
        await planStore.SaveAsync(
            CreatePlan("cache-chapter-1-0", ChapterSpeechPlanState.Ready, []),
            CancellationToken.None);

        var titleKey = TestAudioCacheKey.CreateTitle("book-1", 0, 7, 12, "第一章");
        await InsertIndexedCoverageEntryAsync(
            fixture,
            titleKey,
            "cache-chapter-1-0",
            profile,
            healthState: 1);

        var withTitle = await fixture.Cache.GetCurrentConfigurationStatusesAsync(
            [new CurrentCacheChapterQuery("cache-chapter-1-0", 0, true, Fingerprint.Sha256("第一章"))],
            profile,
            CancellationToken.None);
        var withoutTitle = await fixture.Cache.GetCurrentConfigurationStatusesAsync(
            [new CurrentCacheChapterQuery("cache-chapter-1-0", 0, false, null)],
            profile,
            CancellationToken.None);

        Assert.Equal(new ChapterCacheStatus(0, 1, 1), Assert.Single(withTitle));
        Assert.Equal(ChapterCacheStatusKind.Available, withTitle[0].Kind);
        Assert.Equal(
            new ChapterCacheStatus(0, 0, 0)
            {
                Kind = ChapterCacheStatusKind.NoPlayableContent
            },
            Assert.Single(withoutTitle));
        Assert.Equal(ChapterCacheStatusKind.NoPlayableContent, withoutTitle[0].Kind);
    }

    [Fact]
    public async Task Current_configuration_query_aggregates_two_thousand_plan_segments_with_one_connection()
    {
        var fixture = await CreateFixtureAsync();
        var profile = TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "段落 0").Identity.SynthesisProfile;
        var planStore = new SqliteChapterSpeechPlanStore(fixture.ConnectionFactory);
        var segments = Enumerable
            .Range(0, 2_000)
            .Select(index => CreatePlanSegment(index, index, $"段落 {index}"))
            .ToArray();
        await planStore.SaveAsync(
            CreatePlan("cache-chapter-1-0", ChapterSpeechPlanState.Ready, segments),
            CancellationToken.None);

        var cachedKey = TestAudioCacheKey.Create("book-1", 0, 1_999, 7, 12, "段落 1999");
        await InsertIndexedCoverageEntryAsync(
            fixture,
            cachedKey,
            "cache-chapter-1-0",
            profile,
            healthState: 1,
            filePath: "Cache/Tts/v2/missing-1999.mp3");

        fixture.CacheConnectionFactory.Reset();
        var statuses = await fixture.Cache.GetCurrentConfigurationStatusesAsync(
            [new CurrentCacheChapterQuery("cache-chapter-1-0", 0, false, null)],
            profile,
            CancellationToken.None);

        var status = Assert.Single(statuses);
        Assert.Equal(new ChapterCacheStatus(0, 1, 2_000), status);
        Assert.Equal(1, fixture.CacheConnectionFactory.OpenCount);
    }

    [Fact]
    public async Task GetValidEntriesAsync_counts_only_decodable_files_and_does_not_touch_lru()
    {
        var timeProvider = new ManualTimeProvider();
        var fixture = await CreateFixtureAsync(timeProvider: timeProvider);
        var validKey = TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "当前文本甲");
        var corruptKey = TestAudioCacheKey.Create("book-1", 0, 1, 7, 12, "当前文本乙");
        var missingKey = TestAudioCacheKey.Create("book-1", 0, 2, 7, 12, "当前文本丙");
        var invalidPathKey = TestAudioCacheKey.Create("book-1", 0, 3, 7, 12, "当前文本丁");
        var valid = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                validKey,
                "book-1",
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
        var firstKey = TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "第一段");
        var secondKey = TestAudioCacheKey.Create("book-1", 0, 1, 7, 12, "第二段");
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                firstKey,
                "book-1",
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
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "第一段");
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
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
        var key = TestAudioCacheKey.Create("book-1", 2, 0, 1, 10, "第一段");
        var changes = new List<CacheChangedEventArgs>();
        fixture.Cache.Changed += (_, eventArgs) => changes.Add(eventArgs);

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
                2,
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
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
        var sourceFile = CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path);

        var stored = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key, "book-1", 0, 1, sourceFile, "audio/mpeg"),
            CancellationToken.None);

        Assert.True(File.Exists(stored.FilePath));
        Assert.Contains(Path.Combine("Cache", "Tts", AudioCacheKey.CurrentVersion, key.Shard), stored.FilePath);
        Assert.EndsWith($@"{key.FileNameBase}.mp3", stored.FilePath, StringComparison.OrdinalIgnoreCase);

        var hit = await fixture.Cache.TryGetAsync(key, CancellationToken.None);
        Assert.NotNull(hit);
        Assert.Equal(stored.FilePath, hit!.FilePath);
    }

    [Fact]
    public async Task StoreAsync_rejects_corrupt_audio_before_moving_or_indexing_it()
    {
        var fixture = await CreateFixtureAsync();
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "损坏响应");
        var sourceFile = Path.Combine(fixture.Directories.RootDirectoryPath, "corrupt.mp3");
        await File.WriteAllTextAsync(sourceFile, "not audio", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key, "book-1", 0, 1, sourceFile, "audio/mpeg"),
            CancellationToken.None));

        Assert.True(File.Exists(sourceFile));
        Assert.Equal(0, (await fixture.Cache.GetSummaryAsync(CancellationToken.None)).EntryCount);
    }

    [Fact]
    public async Task StoreAsync_persists_v2_identity_and_real_synthesis_profile_metadata()
    {
        var fixture = await CreateFixtureAsync();
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "带配置身份的正文");

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
                0,
                7,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);

        await using var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT e.KeyVersion, e.SpeechTextHash, e.SynthesisProfileFingerprint,
                   p.SchemaVersion, p.RuleFingerprint, p.SpeakSpeed
            FROM AudioCacheEntries e
            INNER JOIN SynthesisProfiles p ON p.Fingerprint = e.SynthesisProfileFingerprint
            WHERE e.CacheKey = $cacheKey;
            """;
        command.Parameters.AddWithValue("$cacheKey", Encoding.UTF8.GetBytes(key.Value));
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(key.Identity.SpeechTextHash.ToArray(), reader.GetFieldValue<byte[]>(1));
        Assert.Equal(key.Identity.SynthesisProfile.Value.ToArray(), reader.GetFieldValue<byte[]>(2));
        Assert.Equal(key.Identity.SynthesisProfile.SchemaVersion, reader.GetInt32(3));
        Assert.Equal(key.Identity.SynthesisProfile.TtsRule.Value.ToArray(), reader.GetFieldValue<byte[]>(4));
        Assert.Equal(key.Identity.SynthesisProfile.SpeakSpeed, reader.GetInt32(5));
    }

    [Fact]
    public async Task StoreAsync_merges_same_key_writers_without_indexing_until_a_valid_file_exists()
    {
        var fixture = await CreateFixtureAsync();
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 7, 12, "并发同键");
        var first = new AudioCacheWriteRequest(
            key,
            "book-1",
            0,
            7,
            CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
            "audio/mpeg");
        var second = first with { SourceFilePath = CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path) };

        var stored = await Task.WhenAll(
            fixture.Cache.StoreAsync(first, CancellationToken.None),
            fixture.Cache.StoreAsync(second, CancellationToken.None));

        Assert.Equal(2, stored.Length);
        Assert.All(stored, entry => Assert.True(File.Exists(entry.FilePath)));
        Assert.Equal(1, (await fixture.Cache.GetSummaryAsync(CancellationToken.None)).EntryCount);
    }

    [Fact]
    public async Task TryGetAsync_removes_stale_database_entry_when_file_is_missing()
    {
        var fixture = await CreateFixtureAsync();
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
        var stored = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key, "book-1", 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);

        File.Delete(stored.FilePath);

        var hit = await fixture.Cache.TryGetAsync(key, CancellationToken.None);
        var summary = await fixture.Cache.GetSummaryAsync(CancellationToken.None);

        Assert.Null(hit);
        Assert.Equal(0, summary.EntryCount);
    }

    [Fact]
    public async Task TryGetAsync_removes_stale_database_entry_when_file_is_not_decodable()
    {
        var fixture = await CreateFixtureAsync();
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
        var changes = new List<CacheChangedEventArgs>();
        fixture.Cache.Changed += (_, eventArgs) => changes.Add(eventArgs);
        var stored = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);

        await File.WriteAllTextAsync(stored.FilePath, "not audio", CancellationToken.None);

        var hit = await fixture.Cache.TryGetAsync(key, CancellationToken.None);
        var summary = await fixture.Cache.GetSummaryAsync(CancellationToken.None);

        Assert.Null(hit);
        Assert.Equal(0, summary.EntryCount);
        Assert.False(File.Exists(stored.FilePath));
        Assert.Equal([new CacheChangedEventArgs(null, null)], changes.Skip(1).ToArray());
    }

    [Fact]
    public async Task InvalidateAsync_removes_file_and_index()
    {
        var fixture = await CreateFixtureAsync();
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
        var stored = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key, "book-1", 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);

        await fixture.Cache.InvalidateAsync(key, CancellationToken.None);

        Assert.False(File.Exists(stored.FilePath));
        Assert.Null(await fixture.Cache.TryGetAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task GetBooksAndChaptersAsync_and_clear_operations_follow_book_and_chapter_boundaries()
    {
        var fixture = await CreateFixtureAsync();
        var key1 = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
        var key2 = TestAudioCacheKey.Create("book-1", 1, 0, 1, 10, "第二章");
        var key4 = TestAudioCacheKey.Create("book-1", 1, 1, 1, 10, "第二章第二段");
        var key3 = TestAudioCacheKey.Create("book-2", 0, 0, 1, 10, "其他书");

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key1, "book-1", 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key2, "book-1", 1, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key4, "book-1", 1, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key3, "book-2", 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoWavPath), "audio/wav"),
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
        var firstKey = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
        var protectedKey = TestAudioCacheKey.Create("book-1", 1, 0, 1, 10, "第二段");
        var untouchedKey = TestAudioCacheKey.Create("book-1", 2, 0, 1, 10, "第三段");
        var first = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(firstKey, "book-1", 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        var protectedEntry = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(protectedKey, "book-1", 1, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(untouchedKey, "book-1", 2, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
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
        var shardDirectory = Path.Combine(fixture.Directories.CacheDirectoryPath, "Tts", AudioCacheKey.CurrentVersion, "aa");
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
    public async Task RunMaintenanceAsync_removes_missing_and_long_unused_corrupt_entries_with_chapter_notifications()
    {
        var timeProvider = new ManualTimeProvider();
        var fixture = await CreateFixtureAsync(timeProvider: timeProvider);
        var missingKey = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "缺失文件");
        var corruptKey = TestAudioCacheKey.Create("book-1", 1, 0, 1, 10, "损坏文件");
        var missing = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                missingKey,
                "book-1",
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        var corrupt = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                corruptKey,
                "book-1",
                1,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        File.Delete(missing.FilePath);
        File.Copy(PlaybackTestAudio.CorruptMp3Path, corrupt.FilePath, overwrite: true);
        timeProvider.Advance(TimeSpan.FromDays(31));

        var changes = new List<CacheChangedEventArgs>();
        fixture.Cache.Changed += (_, args) => changes.Add(args);

        await fixture.Cache.RunMaintenanceAsync(CancellationToken.None);

        Assert.Null(await fixture.Cache.TryGetAsync(missingKey, CancellationToken.None));
        Assert.Null(await fixture.Cache.TryGetAsync(corruptKey, CancellationToken.None));
        Assert.Contains(new CacheChangedEventArgs("book-1", 0), changes);
        Assert.Contains(new CacheChangedEventArgs("book-1", 1), changes);
    }

    [Fact]
    public async Task RunMaintenanceAsync_does_not_probe_recently_used_corrupt_entry()
    {
        var timeProvider = new ManualTimeProvider();
        var fixture = await CreateFixtureAsync(timeProvider: timeProvider);
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "近期损坏");
        var stored = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);
        File.Copy(PlaybackTestAudio.CorruptMp3Path, stored.FilePath, overwrite: true);

        await fixture.Cache.RunMaintenanceAsync(CancellationToken.None);

        Assert.True(File.Exists(stored.FilePath));
        Assert.Equal(1, (await fixture.Cache.GetSummaryAsync(CancellationToken.None)).EntryCount);
    }

    [Fact]
    public async Task RunMaintenanceAsync_applies_lru_and_skips_protected_files()
    {
        var registry = new AudioCacheProtectionRegistry();
        var limit = new FileInfo(PlaybackTestAudio.DemoMp3Path).Length + 1;
        var fixture = await CreateFixtureAsync(limit, registry);
        var key1 = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
        var key2 = TestAudioCacheKey.Create("book-1", 0, 1, 1, 10, "第二段");

        var first = await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key1, "book-1", 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);

        using var protection = registry.Protect(first.FilePath);

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key2, "book-1", 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoWavPath), "audio/wav"),
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
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(key, "book-1", 0, 1, CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"),
            CancellationToken.None);

        var orphanDirectory = Path.Combine(fixture.Directories.CacheDirectoryPath, "Tts", AudioCacheKey.CurrentVersion, "ff");
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
    public async Task ClearAllAsync_preserves_books_progress_and_current_speech_plans()
    {
        var fixture = await CreateFixtureAsync();
        var progressStore = new SqliteReadingProgressStore(fixture.ConnectionFactory);
        await progressStore.SaveAsync(
            new PlaybackProgressUpdate("book-1", 0, 0, 0, 100),
            CancellationToken.None);
        var planStore = new SqliteChapterSpeechPlanStore(fixture.ConnectionFactory);
        await planStore.SaveAsync(
            CreatePlan(
                "cache-chapter-1-0",
                ChapterSpeechPlanState.Ready,
                [CreatePlanSegment(0, 0, "当前朗读计划")]),
            CancellationToken.None);
        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "当前朗读计划"),
                "book-1",
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);

        await fixture.Cache.ClearAllAsync(CancellationToken.None);

        await using var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM Books),
                (SELECT COUNT(*) FROM Chapters),
                (SELECT COUNT(*) FROM ReadingProgress WHERE BookId = 'book-1'),
                (SELECT COUNT(*) FROM ChapterSpeechPlans WHERE ChapterId = 'cache-chapter-1-0'),
                (SELECT COUNT(*) FROM ChapterSpeechPlanSegments WHERE ChapterId = 'cache-chapter-1-0'),
                (SELECT COUNT(*) FROM AudioCacheEntries);
            """;
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(5, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4));
        Assert.Equal(0, reader.GetInt32(5));
    }

    [Fact]
    public async Task StoreAsync_serializes_concurrent_writes_without_losing_index_entries()
    {
        var fixture = await CreateFixtureAsync();
        var writes = Enumerable.Range(0, 8)
            .Select(segmentIndex => fixture.Cache.StoreAsync(
                new AudioCacheWriteRequest(
                    TestAudioCacheKey.Create("book-1", 0, segmentIndex, 1, 10, $"段落 {segmentIndex}"),
                    "book-1",
                    0,
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
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");

        await fixture.Cache.StoreAsync(
            new AudioCacheWriteRequest(
                key,
                "book-1",
                0,
                1,
                CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path),
                "audio/mpeg"),
            CancellationToken.None);

        await using var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CreatedAt, LastAccessedAt FROM AudioCacheEntries WHERE CacheKey = $cacheKey;";
        command.Parameters.AddWithValue("$cacheKey", Encoding.UTF8.GetBytes(key.Value));
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("2026-07-16T01:08:07.0000000+00:00", reader.GetString(0));
        Assert.Equal("2026-07-16T01:08:07.0000000+00:00", reader.GetString(1));
    }

    [Fact]
    public async Task StoreAsync_pre_cancelled_does_not_consume_source_or_leave_cache_files()
    {
        var fixture = await CreateFixtureAsync();
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
        var sourceFile = CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Cache.StoreAsync(
                new AudioCacheWriteRequest(key, "book-1", 0, 1, sourceFile, "audio/mpeg"),
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
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
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
        var key = TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "第一段");
        var outsidePath = Path.Combine(Path.GetDirectoryName(fixture.Directories.RootDirectoryPath)!, "outside.mp3");

        await using (var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR IGNORE INTO SynthesisProfiles
                    (Fingerprint, SchemaVersion, RuleId, RuleFingerprint, SpeakSpeed, CreatedAt)
                VALUES (zeroblob(32), 1, 1, zeroblob(32), 10, $now);
                INSERT INTO AudioCacheEntries (
                    CacheKey, KeyVersion, BookId, ChapterId, SegmentKind, SourceStartOffset, SourceLength,
                    SpeechTextHash, SynthesisProfileFingerprint, FilePath, ContentType, FileSize,
                    DurationMilliseconds, HealthState, ValidatedAt, CreatedAt, LastAccessedAt)
                VALUES ($cacheKey, 2, $bookId, 'cache-chapter-1-0', 0, 0, 1,
                    zeroblob(32), zeroblob(32), $filePath, 'audio/mpeg', 1, NULL, 1, $now, $now, $now);
                """;
            command.Parameters.AddWithValue("$cacheKey", Encoding.UTF8.GetBytes(key.Value));
            command.Parameters.AddWithValue("$bookId", "book-1");
            command.Parameters.AddWithValue("$filePath", outsidePath);
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Cache.TryGetAsync(key, CancellationToken.None));
    }

    private static ChapterSpeechPlan CreatePlan(
        string chapterId,
        ChapterSpeechPlanState state,
        IReadOnlyList<ChapterSpeechPlanSegment> segments) =>
        new(
            chapterId,
            Fingerprint.Sha256($"{chapterId}-revision"),
            TextProfileFingerprint.Create(TextSegmentationOptions.Default, []),
            Fingerprint.Sha256($"{chapterId}-plan-{segments.Count}"),
            state,
            segments.Count,
            DateTimeOffset.UtcNow,
            segments);

    private static ChapterSpeechPlanSegment CreatePlanSegment(
        int orderIndex,
        int sourceStartOffset,
        string speechText) =>
        new(
            orderIndex,
            SpeechSegmentKind.Body,
            sourceStartOffset,
            1,
            Fingerprint.Sha256(speechText));

    private static async Task InsertIndexedCoverageEntryAsync(
        CacheFixture fixture,
        AudioCacheKey key,
        string chapterId,
        SynthesisProfileFingerprint profile,
        int healthState,
        string? filePath = null)
    {
        await using var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO SynthesisProfiles
                (Fingerprint, SchemaVersion, RuleId, RuleFingerprint, SpeakSpeed, OptionsJson, CreatedAt)
            VALUES
                ($profile, $schemaVersion, $ruleId, $ruleFingerprint, $speakSpeed, $optionsJson, $now);
            INSERT INTO AudioCacheEntries (
                CacheKey, KeyVersion, BookId, ChapterId, SegmentKind, SourceStartOffset, SourceLength,
                SpeechTextHash, SynthesisProfileFingerprint, FilePath, ContentType, FileSize,
                DurationMilliseconds, HealthState, ValidatedAt, CreatedAt, LastAccessedAt)
            VALUES (
                $cacheKey, 2, 'book-1', $chapterId, $segmentKind, $sourceStartOffset, $sourceLength,
                $speechTextHash, $profile, $filePath, 'audio/mpeg', 1,
                NULL, $healthState, $now, $now, $now);
            """;
        var now = DateTime.UtcNow.ToString("O");
        command.Parameters.AddWithValue("$profile", profile.Value.ToArray());
        command.Parameters.AddWithValue("$schemaVersion", profile.SchemaVersion);
        command.Parameters.AddWithValue("$ruleId", 7);
        command.Parameters.AddWithValue("$ruleFingerprint", profile.TtsRule.Value.ToArray());
        command.Parameters.AddWithValue("$speakSpeed", profile.SpeakSpeed);
        command.Parameters.AddWithValue("$optionsJson", (object?)profile.OptionsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$cacheKey", Encoding.UTF8.GetBytes(key.Value));
        command.Parameters.AddWithValue("$chapterId", chapterId);
        command.Parameters.AddWithValue("$segmentKind", (int)key.Identity.Segment.Kind);
        command.Parameters.AddWithValue("$sourceStartOffset", key.Identity.Segment.SourceStartOffset);
        command.Parameters.AddWithValue(
            "$sourceLength",
            Math.Max(1, key.Identity.Segment.SourceLength));
        command.Parameters.AddWithValue("$speechTextHash", key.Identity.SpeechTextHash.ToArray());
        command.Parameters.AddWithValue("$filePath", filePath ?? "Cache/Tts/v2/missing.mp3");
        command.Parameters.AddWithValue("$healthState", healthState);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
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

        await using (var seedConnection = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            var seedCommand = seedConnection.CreateCommand();
            seedCommand.CommandText =
                """
                INSERT INTO Books
                    (Id, Title, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, UpdatedAt)
                VALUES
                    ('book-1', '书一', 'book-1.txt', 'Books/book-1/content.txt', 'cache-fixture-book-1', 'utf-8', '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00'),
                    ('book-2', '书二', 'book-2.txt', 'Books/book-2/content.txt', 'cache-fixture-book-2', 'utf-8', '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
                INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, StartOffset, Length)
                VALUES
                    ('cache-chapter-1-0', 'book-1', 0, 0, '第一章', 0, 1),
                    ('cache-chapter-1-1', 'book-1', 1, 1, '第二章', 0, 1),
                    ('cache-chapter-1-2', 'book-1', 2, 2, '第三章', 0, 1),
                    ('cache-chapter-1-3', 'book-1', 3, 3, '第四章', 0, 1),
                    ('cache-chapter-2-0', 'book-2', 0, 0, '第一章', 0, 1);
                """;
            await seedCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }

        registry ??= new AudioCacheProtectionRegistry();
        var limitProvider = new MutableAudioCacheLimitProvider
        {
            CurrentLimitBytes = cacheLimitBytes ?? AppSettings.DefaultCacheLimitBytes
        };
        var pathResolver = new AppStoragePathResolver(directories);
        var cacheConnectionFactory = new CountingSqliteConnectionFactory(factory);
        var index = new SqliteAudioCacheIndex(cacheConnectionFactory, timeProvider ?? TimeProvider.System);
        var fileStore = new AudioCacheFileStore(directories, pathResolver, registry);
        var maintenance = new AudioCacheMaintenance(index, fileStore, limitProvider, registry, timeProvider);
        var cache = new AudioCacheFacade(index, fileStore, maintenance, registry, new AudioProbe());
        return new CacheFixture(directories, factory, cacheConnectionFactory, cache, limitProvider);
    }

    private static async Task<string> ReadLastAccessedAtAsync(CacheFixture fixture, AudioCacheKey key)
    {
        await using var connection = await fixture.ConnectionFactory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT LastAccessedAt FROM AudioCacheEntries WHERE CacheKey = $cacheKey;";
        command.Parameters.AddWithValue("$cacheKey", Encoding.UTF8.GetBytes(key.Value));
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
                INSERT OR IGNORE INTO SynthesisProfiles
                    (Fingerprint, SchemaVersion, RuleId, RuleFingerprint, SpeakSpeed, CreatedAt)
                VALUES (zeroblob(32), 1, 7, zeroblob(32), 10, $now);
                INSERT INTO AudioCacheEntries (
                    CacheKey, KeyVersion, BookId, ChapterId, SegmentKind, SourceStartOffset, SourceLength,
                    SpeechTextHash, SynthesisProfileFingerprint, FilePath, ContentType, FileSize,
                    DurationMilliseconds, HealthState, ValidatedAt, CreatedAt, LastAccessedAt)
                VALUES ($cacheKey, 2, 'book-1', 'cache-chapter-1-0', 0, 3, 1,
                    zeroblob(32), zeroblob(32), $filePath, 'audio/mpeg', 1, NULL, 1, $now, $now, $now);
                """;
        command.Parameters.AddWithValue("$cacheKey", Encoding.UTF8.GetBytes(key.Value));
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
