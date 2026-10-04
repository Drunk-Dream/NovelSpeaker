using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.FileSystem.Cache;
using NovelSpeaker.Infrastructure.Cache;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.Infrastructure.Persistence.Cache;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.Infrastructure.IntegrationTests;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class BookLibraryPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removal_excludes_late_audio_write_between_file_snapshot_and_database_commit(bool removeSource)
    {
        var fixture = await CreateFixtureAsync();
        var contentPath = await SeedBookAsync(fixture, "book-1", "并发删除", null);
        if (removeSource) await SeedFutureSourceAsync(fixture, true);
        var resolver = new AppStoragePathResolver(fixture.Directories);
        var journal = new SqliteBookOperationJournal(fixture.Factory, TimeProvider.System);
        var store = new DeferredDeletionStore(new BookDeletionOperationStore(fixture.Factory, fixture.Directories,
            fixture.ProtectionRegistry, resolver, journal, TimeProvider.System));
        var service = new Application.Books.Library.BookDeletionService(store, new BookMutationGate(), fixture.Changes, [], [fixture.Cache]);
        Task removal = removeSource
            ? service.RemoveAsync(new("book-1", "local:book-1"), CancellationToken.None)
            : service.DeleteAsync(new("book-1", true), CancellationToken.None);
        await store.SnapshotReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var lateWrite = fixture.Cache.StoreAsync(new AudioCacheWriteRequest(
            TestAudioCacheKey.Create("book-1", 0, 5, 1, 10, "迟到正文"), "book-1", 0, 1,
            CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"), CancellationToken.None);
        Assert.False(lateWrite.IsCompleted);
        store.AllowCommit.TrySetResult();
        await removal.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidDataException>(() => lateWrite);
        Assert.False(File.Exists(contentPath));
        Assert.Empty(Directory.EnumerateFiles(fixture.Directories.CacheDirectoryPath, "*.mp3", SearchOption.AllDirectories));
        Assert.Empty(await journal.GetIncompleteAsync(CancellationToken.None));
        await AssertTablesEmptyAsync(fixture, "AudioCacheEntries", "ChapterSpeechPlans", "ChapterSpeechPlanSegments");
    }

    [Fact]
    public async Task Removing_last_source_deletes_book_and_internal_content()
    {
        var fixture = await CreateFixtureAsync();
        var path = await SeedBookAsync(fixture, "book-1", "最后来源", null);
        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, change) => changes.Add(change);
        var result = await ((IBookSourceRemovalService)fixture.Deletion).RemoveAsync(
            new("book-1", "local:book-1"), CancellationToken.None);
        Assert.True(result!.DeletedBook);
        Assert.Equal<BookCommittedChange>([
            new BookCommittedChange.SourceRemoved("book-1", "local:book-1"),
            new BookCommittedChange.BookRemoved("book-1")], changes);
        Assert.Null(await fixture.DetailsQuery.GetHeaderAsync("book-1", CancellationToken.None));
        Assert.False(File.Exists(path));
        await AssertTablesEmptyAsync(fixture, "Books", "BookSources", "LocalBookSources", "Chapters", "LocalChapterContents");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Removing_local_source_preserves_other_source_and_book_without_fallback(bool active)
    {
        var fixture = await CreateFixtureAsync();
        var path = await SeedBookAsync(fixture, "book-1", "最后显示快照", "作者", "保留简介");
        await SeedFutureSourceAsync(fixture, true);
        await fixture.ProgressStore.SaveAsync(new("book-1", 0, 0, 1, 0), CancellationToken.None);
        var cache = await fixture.Cache.StoreAsync(new AudioCacheWriteRequest(
            TestAudioCacheKey.Create("book-1", 0, 0, 1, 10, "正文"), "book-1", 0, 1,
            CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), "audio/mpeg"), CancellationToken.None);
        // An unrelated source has its own technical ChapterId and physical audio file.
        var otherFile = Path.Combine(fixture.Directories.CacheDirectoryPath, "other.mp3");
        File.Copy(cache.FilePath, otherFile);
        await ExecuteAsync(fixture, """
            INSERT INTO AudioCacheEntries (CacheKey, BookId, ChapterId, SpeechTextHash, SynthesisProfileFingerprint, FilePath, FileSize, CreatedAt, LastAccessedAt)
            SELECT x'1234', BookId, 'future-chapter', SpeechTextHash, SynthesisProfileFingerprint, $file, FileSize, CreatedAt, LastAccessedAt FROM AudioCacheEntries LIMIT 1;
            INSERT INTO ChapterSpeechPlans VALUES ('future-chapter', zeroblob(32), zeroblob(32), zeroblob(32), 1, 1, 'time');
            """, ("$file", otherFile));
        if (!active) await ExecuteAsync(fixture, "UPDATE Books SET ActiveSourceId = 'future-source' WHERE Id = 'book-1';");

        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, _) => throw new InvalidOperationException("observer failed");
        fixture.Changes.Changed += (_, change) => changes.Add(change);
        var result = await ((IBookSourceRemovalService)fixture.Deletion).RemoveAsync(new("book-1", "local:book-1"), CancellationToken.None);

        Assert.False(result!.DeletedBook);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(cache.FilePath));
        Assert.True(File.Exists(otherFile));
        var header = (await fixture.DetailsQuery.GetHeaderAsync("book-1", CancellationToken.None))!;
        Assert.Equal("最后显示快照", header.Title);
        Assert.Equal("保留简介", header.Description);
        Assert.NotNull(await fixture.ProgressStore.GetAsync("book-1", CancellationToken.None));
        await AssertTablesEmptyAsync(fixture, "LocalBookSources", "LocalChapterContents");
        if (active)
        {
            Assert.Null(header.ActiveSource);
            Assert.Empty(await fixture.DetailsQuery.GetCatalogAsync("book-1", CancellationToken.None));
            var summary = Assert.Single(await fixture.Query.GetBooksAsync(CancellationToken.None));
            Assert.Equal(header.Title, summary.Title);
            Assert.Null(summary.SourceContext);
            Assert.Equal<BookCommittedChange>([
                new BookCommittedChange.SourceRemoved("book-1", "local:book-1"),
                new BookCommittedChange.ActiveSourceChanged("book-1", "local:book-1", null)], changes);
        }
        else
        {
            Assert.Equal("future-source", header.ActiveSource!.Context.SourceId);
            Assert.Single(await fixture.DetailsQuery.GetCatalogAsync("book-1", CancellationToken.None));
            Assert.Equal(new BookCommittedChange.SourceRemoved("book-1", "local:book-1"), Assert.Single(changes));
        }
        await using var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None);
        using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM AudioCacheEntries WHERE ChapterId = 'future-chapter';";
        Assert.Equal(1L, await verify.ExecuteScalarAsync());
        verify.CommandText = "SELECT COUNT(*) FROM ChapterSpeechPlans WHERE ChapterId = 'future-chapter';";
        Assert.Equal(1L, await verify.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_removal_recovery_uses_source_commit_even_while_book_survives(bool commit)
    {
        var fixture = await CreateFixtureAsync();
        var path = await SeedBookAsync(fixture, "book-1", "恢复", null);
        await SeedFutureSourceAsync(fixture, true);
        var resolver = new AppStoragePathResolver(fixture.Directories);
        var journal = new SqliteBookOperationJournal(fixture.Factory, TimeProvider.System);
        var store = new BookDeletionOperationStore(fixture.Factory, fixture.Directories, fixture.ProtectionRegistry,
            resolver, journal, TimeProvider.System);
        var preparation = (await store.BeginSourceRemovalAsync(new("book-1", "local:book-1"), CancellationToken.None))!;
        Assert.False(File.Exists(path));
        if (commit) await store.CommitAsync(preparation, CancellationToken.None);
        var recovery = new BookOperationRecoveryService(fixture.Factory, journal, resolver, fixture.Directories);
        await recovery.RecoverAsync(CancellationToken.None);
        await recovery.RecoverAsync(CancellationToken.None);
        Assert.Equal(!commit, File.Exists(path));
        Assert.NotNull(await fixture.DetailsQuery.GetHeaderAsync("book-1", CancellationToken.None));
        Assert.Empty(await journal.GetIncompleteAsync(CancellationToken.None));
        Assert.Equal(commit, (await fixture.DetailsQuery.GetHeaderAsync("book-1", CancellationToken.None))!.ActiveSource is null);
    }

    [DirectoryLinkFact]
    public async Task Delete_rejects_nested_reparse_point_and_preserves_external_content()
    {
        var fixture = await CreateFixtureAsync();
        var path = await SeedBookAsync(fixture, "book-1", "链接边界", null);
        var external = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(external);
        var externalFile = Path.Combine(external, "keep.txt");
        await File.WriteAllTextAsync(externalFile, "external");
        var link = Path.Combine(Path.GetDirectoryName(path)!, "nested");
        DirectoryLinkTestHelper.CreateDirectoryLink(link, external);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Deletion.DeleteAsync(new("book-1", true), CancellationToken.None));
            Assert.True(File.Exists(path));
            Assert.Equal("external", await File.ReadAllTextAsync(externalFile));
            Assert.NotNull(await fixture.DetailsQuery.GetHeaderAsync("book-1", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(external, true);
        }
    }

    [Fact]
    public async Task Source_delete_failure_restores_active_content_catalog_and_journal()
    {
        var fixture = await CreateFixtureAsync();
        var path = await SeedBookAsync(fixture, "book-1", "失败保留", null);
        await SeedFutureSourceAsync(fixture, true);
        await ExecuteAsync(fixture, """
            CREATE TRIGGER BlockSourceDelete BEFORE DELETE ON BookSources
            BEGIN SELECT RAISE(ABORT, 'blocked'); END;
            """);
        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, change) => changes.Add(change);
        await Assert.ThrowsAsync<SqliteException>(() => ((IBookSourceRemovalService)fixture.Deletion).RemoveAsync(
            new("book-1", "local:book-1"), CancellationToken.None));
        Assert.True(File.Exists(path));
        Assert.Empty(changes);
        Assert.Equal("local:book-1", (await fixture.DetailsQuery.GetHeaderAsync("book-1", CancellationToken.None))!.ActiveSource!.Context.SourceId);
        Assert.Equal(2, (await fixture.DetailsQuery.GetCatalogAsync("book-1", CancellationToken.None)).Count);
        Assert.Empty(await new SqliteBookOperationJournal(fixture.Factory, TimeProvider.System).GetIncompleteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Queries_and_delete_return_null_when_book_is_missing()
    {
        var fixture = await CreateFixtureAsync();

        Assert.Null(await fixture.DetailsQuery.GetHeaderAsync("missing", CancellationToken.None));
        Assert.Null(await fixture.DetailsQuery.GetStatisticsAsync("missing", CancellationToken.None));
        Assert.Null(await fixture.Deletion.DeleteAsync(new BookDeleteRequest("missing", true), CancellationToken.None));
    }

    [Fact]
    public async Task Independent_detail_queries_and_UpdateMetadataAsync_return_expected_projections()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", title: "原书名", author: null, description: "旧简介");
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

        var header = await fixture.DetailsQuery.GetHeaderAsync("book-1", CancellationToken.None);
        var catalog = await fixture.DetailsQuery.GetCatalogAsync("book-1", CancellationToken.None);
        var readingPosition = await fixture.DetailsQuery.GetReadingPositionAsync("book-1", CancellationToken.None);
        var statistics = await fixture.DetailsQuery.GetStatisticsAsync("book-1", CancellationToken.None);
        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, _) => throw new InvalidOperationException("observer failed");
        fixture.Changes.Changed += (_, change) => changes.Add(change);
        var updated = await fixture.Metadata.UpdateMetadataAsync(
            new BookMetadataUpdateRequest("book-1", "  新书名  ", "  作者甲  "),
            CancellationToken.None);

        Assert.NotNull(header);
        Assert.Equal("原书名", header!.Title);
        Assert.Null(header.Author);
        Assert.Equal("旧简介", header.Description);
        Assert.Equal(2, catalog.Count);
        Assert.Equal(1, readingPosition!.ChapterIndex);
        Assert.NotNull(statistics);
        Assert.True(statistics!.CachedAudioBytes > 0);
        Assert.Equal("新书名", updated.Title);
        Assert.Equal("作者甲", updated.Author);
        Assert.Equal("旧简介", updated.Description);
        var persisted = await fixture.DetailsQuery.GetHeaderAsync("book-1", CancellationToken.None);
        Assert.Equal(updated.Title, persisted!.Title);
        Assert.Equal(updated.Author, persisted.Author);
        Assert.Equal(new BookCommittedChange.MetadataCommitted("book-1"), Assert.Single(changes));
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
        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, change) => changes.Add(change);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Metadata.UpdateMetadataAsync(
            new BookMetadataUpdateRequest("missing", "新书名", "新作者"),
            CancellationToken.None));

        var unchanged = await fixture.DetailsQuery.GetHeaderAsync("book-1", CancellationToken.None);
        Assert.NotNull(unchanged);
        Assert.Equal("保留书名", unchanged.Title);
        Assert.Equal("保留作者", unchanged.Author);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task DeleteAsync_removes_book_progress_and_internal_files()
    {
        var fixture = await CreateFixtureAsync();
        var storedFilePath = await SeedBookAsync(fixture, "book-1", title: "待删除书籍", author: "作者");
        await fixture.ProgressStore.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 120), CancellationToken.None);

        var result = await fixture.Deletion.DeleteAsync(new BookDeleteRequest("book-1", false), CancellationToken.None);
        var remainingDetails = await fixture.DetailsQuery.GetStatisticsAsync("book-1", CancellationToken.None);
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
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO ChapterSpeechPlans
                    (ChapterId, ChapterRevisionHash, TextProfileFingerprint, PlanOutputHash, State, BodySegmentCount, UpdatedAt)
                VALUES
                    ('book-1/chapter/0', zeroblob(32), zeroblob(32), zeroblob(32), 1, 1, $now),
                    ('book-1/chapter/1', zeroblob(32), zeroblob(32), zeroblob(32), 1, 1, $now);
                INSERT INTO ChapterSpeechPlanSegments
                    (ChapterId, OrderIndex, SegmentKind, SourceStartOffset, SourceLength, SpeechTextHash)
                VALUES
                    ('book-1/chapter/0', 0, 0, 0, 1, zeroblob(32)),
                    ('book-1/chapter/1', 0, 0, 0, 1, zeroblob(32));
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
                     "BookSources",
                     "LocalBookSources",
                     "Chapters",
                     "LocalChapterContents",
                     "ReadingProgress",
                     "ChapterSpeechPlans",
                     "ChapterSpeechPlanSegments",
                     "AudioCacheEntries"
                 })
        {
            using var command = verifyConnection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(CancellationToken.None))!);
        }
    }

    [Fact]
    public async Task DeleteAsync_restores_staged_files_when_database_delete_fails()
    {
        var fixture = await CreateFixtureAsync();
        var storedFilePath = await SeedBookAsync(fixture, "book-1", title: "触发回滚", author: null);
        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, change) => changes.Add(change);

        await using (var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None))
        {
            using var trigger = connection.CreateCommand();
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
        Assert.Empty(changes);
        Assert.NotNull(await fixture.DetailsQuery.GetStatisticsAsync("book-1", CancellationToken.None));
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
        Assert.NotNull(await fixture.DetailsQuery.GetStatisticsAsync("book-1", CancellationToken.None));
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

        Assert.NotNull(await fixture.DetailsQuery.GetStatisticsAsync("book-1", CancellationToken.None));
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
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE LocalBookSources SET StoredContentPath = $path WHERE SourceId = 'local:book-1';";
            command.Parameters.AddWithValue("$path", externalPath);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Deletion.DeleteAsync(new BookDeleteRequest("book-1", false), CancellationToken.None));

        Assert.True(File.Exists(externalPath));
        Assert.Equal("external source", await File.ReadAllTextAsync(externalPath, CancellationToken.None));
        Assert.NotNull(await fixture.DetailsQuery.GetStatisticsAsync("book-1", CancellationToken.None));
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
            using var command = connection.CreateCommand();
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
            Assert.NotNull(await fixture.DetailsQuery.GetStatisticsAsync("book-1", CancellationToken.None));
        }
        finally
        {
            File.Delete(externalPath);
        }
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
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
        var planStore = new SqliteChapterSpeechPlanStore(factory);
        var maintenance = new AudioCacheMaintenance(
            index,
            fileStore,
            limitProvider,
            protectionRegistry,
            planStore);
        var cache = new AudioCacheFacade(index, fileStore, maintenance, protectionRegistry, new AudioProbe());
        var progressStore = new SqliteReadingProgressStore(factory);
        var query = new BookLibraryQuery(factory);
        var detailsQuery = new BookDetailsQuery(factory);
        var journal = new SqliteBookOperationJournal(factory, TimeProvider.System);
        var deletionStore = new BookDeletionOperationStore(
            factory,
            directories,
            protectionRegistry,
            pathResolver,
            journal,
            TimeProvider.System);
        var changes = new BookSourceChanges();
        var mutations = new BookMutationGate();
        var metadata = new Application.Books.Library.BookMetadataUpdateService(new SqliteBookMetadataStore(factory), mutations, changes);
        var deletion = new Application.Books.Library.BookDeletionService(deletionStore, mutations, changes, [], [cache]);
        return new TestFixture(directories, factory, cache, progressStore, protectionRegistry, query, detailsQuery, metadata, deletion, changes);
    }

    private static async Task<string> SeedBookAsync(
        TestFixture fixture,
        string bookId,
        string title,
        string? author,
        string? description = null)
    {
        var storedDirectory = Path.Combine(fixture.Directories.BooksDirectoryPath, bookId);
        Directory.CreateDirectory(storedDirectory);
        var storedFilePath = Path.Combine(storedDirectory, "content.txt");
        await File.WriteAllTextAsync(storedFilePath, "第一章 第一段第二章 第一段", CancellationToken.None);

        await SourceBookFixture.SaveAsync(fixture.Factory, bookId, ["第一章", "第二章"], storedFilePath,
            title: title, author: author, description: description,
            chapterIds: [$"{bookId}/chapter/0", $"{bookId}/chapter/1"]);

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
        AppDataDirectoryProvider Directories,
        SqliteConnectionFactory Factory,
        AudioCacheFacade Cache,
        SqliteReadingProgressStore ProgressStore,
        AudioCacheProtectionRegistry ProtectionRegistry,
        BookLibraryQuery Query,
        BookDetailsQuery DetailsQuery,
        IBookMetadataUpdateService Metadata,
        IBookDeletionService Deletion,
        BookSourceChanges Changes);

    private static Task SeedFutureSourceAsync(TestFixture fixture, bool localActive) => ExecuteAsync(fixture, """
        -- Exercise the generic lifecycle without introducing Online schema into production.
        PRAGMA ignore_check_constraints = ON;
        INSERT INTO BookSources VALUES ('future-source', 'book-1', 2, '其它来源', NULL, NULL, 'time', 'time');
        PRAGMA ignore_check_constraints = OFF;
        INSERT INTO Chapters VALUES ('future-chapter', 'future-source', 0, 0, '其它章节');
        UPDATE Books SET ActiveSourceId = $active WHERE Id = 'book-1';
        """, ("$active", localActive ? "local:book-1" : "future-source"));

    private static async Task ExecuteAsync(TestFixture fixture, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertTablesEmptyAsync(TestFixture fixture, params string[] tables)
    {
        await using var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None);
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
    }

    private sealed class DeferredDeletionStore(IBookDeletionOperationStore inner) : IBookDeletionOperationStore
    {
        public TaskCompletionSource SnapshotReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BookDeletionPreparation?> BeginAsync(BookDeleteRequest request, CancellationToken cancellationToken) => inner.BeginAsync(request, cancellationToken);
        public Task<BookDeletionPreparation?> BeginSourceRemovalAsync(BookSourceRemoveRequest request, CancellationToken cancellationToken) => inner.BeginSourceRemovalAsync(request, cancellationToken);
        public async Task CommitAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken)
        {
            SnapshotReady.TrySetResult();
            await AllowCommit.Task.WaitAsync(cancellationToken);
            await inner.CommitAsync(preparation, cancellationToken);
        }
        public Task CompleteAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken) => inner.CompleteAsync(preparation, cancellationToken);
        public Task RollbackAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken) => inner.RollbackAsync(preparation, cancellationToken);
    }

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
