using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.Import;
using NovelSpeaker.Application.Books.Library;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.Books.Text;
using NovelSpeaker.Infrastructure.Cache;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class BookImportRepositoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_resolves_identity_again_after_a_book_is_removed_during_preparation(bool removeSource)
    {
        var analyzer = new PausingAnalyzer();
        using var fixture = await Fixture.CreateAsync(analyzer);
        var original = await fixture.ImportAsync("Fixture.txt", "old body");
        var target = (await fixture.Repository.GetTargetAsync(original.ImportedBook!.BookId, CancellationToken.None))!;
        var paused = fixture.ImportAsync("Paused.txt", "new body", "Fixture");
        try
        {
            await analyzer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (removeSource)
                await fixture.Deletion.RemoveAsync(new BookSourceRemoveRequest(target.Book.Id, target.Binding!.BindingId),
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            else
                await fixture.Deletion.DeleteAsync(new BookDeleteRequest(target.Book.Id, true),
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            analyzer.Continue.TrySetResult();
            await paused;
        }
        var imported = await paused;
        Assert.Equal(DirectBookImportStatus.Imported, imported.Status);
        Assert.NotEqual(target.Book.Id, imported.ImportedBook!.BookId);
        Assert.Null(await fixture.Repository.GetTargetAsync(target.Book.Id, CancellationToken.None));
        Assert.Equal("new body", await fixture.ReadContentAsync(
            (await fixture.Repository.GetTargetAsync(imported.ImportedBook.BookId, CancellationToken.None))!));
        Assert.Single(Directory.GetFiles(fixture.Directories.BooksDirectoryPath, "*", SearchOption.AllDirectories));
        Assert.Empty(await fixture.Journal.GetIncompleteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_during_import_preparation_leaves_the_existing_snapshot_intact()
    {
        var analyzer = new PausingAnalyzer();
        using var fixture = await Fixture.CreateAsync(analyzer);
        using var cancellation = new CancellationTokenSource();
        var original = await fixture.ImportAsync("Fixture.txt", "old body");
        var target = (await fixture.Repository.GetTargetAsync(original.ImportedBook!.BookId, CancellationToken.None))!;
        var path = Path.Combine(fixture.Root, "Paused.txt");
        await File.WriteAllTextAsync(path, "new body");
        var paused = fixture.ImportRequestAsync(new DirectBookImportRequest(path, "utf-8", "Paused.txt", new("Fixture", "")), cancellation.Token);
        try
        {
            await analyzer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => paused);
        }
        Assert.Equal(target, await fixture.Repository.GetTargetAsync(target.Book.Id, CancellationToken.None));
        Assert.Equal("old body", await fixture.ReadContentAsync(target));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Directories.BooksDirectoryPath, target.Book.Id)));
        Assert.Empty(await fixture.Journal.GetIncompleteAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_imports_resolve_the_latest_snapshot_and_do_not_duplicate_identity(bool existing)
    {
        var analyzer = new PausingAnalyzer();
        using var fixture = await Fixture.CreateAsync(analyzer);
        var id = existing ? (await fixture.ImportAsync("Fixture.txt", "old body")).ImportedBook!.BookId : null;
        var paused = fixture.ImportAsync("Paused.txt", "final body", "Fixture");
        try
        {
            await analyzer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var other = await fixture.ImportAsync("Other.txt", "intermediate body", " Fixture ").WaitAsync(TimeSpan.FromSeconds(10));
            id ??= other.ImportedBook!.BookId;
            Assert.Equal(id, other.ImportedBook!.BookId);
        }
        finally
        {
            analyzer.Continue.TrySetResult();
            await paused;
        }
        Assert.Equal(id, (await paused).ImportedBook!.BookId);
        var target = (await fixture.Repository.GetTargetAsync(id!, CancellationToken.None))!;
        Assert.Equal("final body", await fixture.ReadContentAsync(target));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Directories.BooksDirectoryPath, id!)));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM Books;"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM BookSourceBindings;"));
        Assert.Empty(await fixture.Journal.GetIncompleteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Import_creates_a_complete_source_snapshot_independent_of_the_external_file()
    {
        using var fixture = await Fixture.CreateAsync();
        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, _) => throw new InvalidOperationException("observer failed");
        fixture.Changes.Changed += (_, change) => changes.Add(change);
        var result = await fixture.ImportAsync("Fixture.txt", "first body");
        var target = await fixture.Repository.GetTargetAsync(result.ImportedBook!.BookId, CancellationToken.None);
        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
        Assert.Equal(target!.Binding!.BindingId, target.Book.ActiveSourceBindingId);
        Assert.Equal<BookCommittedChange>([
            new BookCommittedChange.MetadataCommitted(target.Book.Id),
            new BookCommittedChange.ActiveSourceChanged(target.Book.Id, null, target.Binding.BindingId),
            new BookCommittedChange.ActiveCatalogCommitted(target.Book.Id, target.Binding.BindingId, await fixture.StringScalarAsync("SELECT Id FROM Chapters;"))], changes);
        Assert.Equal("Fixture", target.Book.Title);
        Assert.Equal("utf-8", target.LocalBinding!.Encoding);
        Assert.Equal(target.LocalBinding.ImportedAt, target.LocalBinding.LastImportedAt);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM LocalChapterContents;"));
        Assert.Equal("first body", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Fixture.txt")));
        File.Delete(Path.Combine(fixture.Root, "Fixture.txt"));
        Assert.Equal("first body", await fixture.ReadContentAsync(target));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM Chapters WHERE SourceBindingId = '" + target.Binding.BindingId + "';"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reimport_keeps_book_and_source_identity_and_only_projects_active_metadata(bool active)
    {
        using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ImportAsync("Fixture.txt", "old body");
        var id = first.ImportedBook!.BookId;
        var old = (await fixture.Repository.GetTargetAsync(id, CancellationToken.None))!;
        var chapterId = await fixture.StringScalarAsync("SELECT Id FROM Chapters;");
        await fixture.ExecuteAsync("UPDATE Books SET Description = 'old description';");
        await fixture.ExecuteAsync($"""
            INSERT INTO ReadingProgress(BookId, ChapterIndex, SegmentIndex, CharacterOffset, AudioPositionMilliseconds, UpdatedAt)
            VALUES('{id}', 7, 2, 9, 123, 'now');
            INSERT INTO ChapterSpeechPlans(ChapterId, ChapterRevisionHash, TextProfileFingerprint, PlanOutputHash, State, BodySegmentCount, UpdatedAt)
            VALUES('{chapterId}', X'01', X'02', X'03', 1, 1, 'now');
            """);
        if (!active)
        {
            await fixture.ExecuteAsync("DELETE FROM Chapters; UPDATE Books SET ActiveSourceBindingId = NULL;");
        }

        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, change) => changes.Add(change);
        var second = await fixture.ImportAsync("Renamed.txt", "new body", "Fixture");
        var updated = (await fixture.Repository.GetTargetAsync(id, CancellationToken.None))!;
        Assert.Equal(id, second.ImportedBook?.BookId);
        Assert.Equal(old.Binding!.BindingId, updated.Binding!.BindingId);
        Assert.Equal("Fixture", updated.Book.Title);
        Assert.Equal("Renamed.txt", updated.LocalBinding!.OriginalFileName);
        Assert.Equal(active ? string.Empty : "old description", updated.Book.Description ?? string.Empty);
        Assert.Equal(active ? updated.Binding.BindingId : null, updated.Book.ActiveSourceBindingId);
        Assert.Equal(old.Book.ImportedAt, updated.Book.ImportedAt);
        Assert.Equal(old.LocalBinding!.ImportedAt, updated.LocalBinding!.ImportedAt);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM BookSourceBindings;"));
        Assert.Equal(7L, await fixture.ScalarAsync("SELECT ChapterIndex FROM ReadingProgress;"));
        Assert.Equal(123L, await fixture.ScalarAsync("SELECT AudioPositionMilliseconds FROM ReadingProgress;"));
        if (active)
            Assert.NotEqual(chapterId, await fixture.StringScalarAsync("SELECT Id FROM Chapters;"));
        else
            Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM Chapters;"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ChapterSpeechPlans;"));
        Assert.Equal("new body", await fixture.ReadContentAsync(updated));
        Assert.False(File.Exists(fixture.Resolver.ResolvePath(old.LocalBinding.StoredContentPath)));
        if (active)
            Assert.Equal<BookCommittedChange>([
                new BookCommittedChange.MetadataCommitted(id),
                new BookCommittedChange.ActiveCatalogCommitted(id, updated.Binding.BindingId, await fixture.StringScalarAsync("SELECT Id FROM Chapters;"))], changes);
        else Assert.Empty(changes);
    }

    [Fact]
    public async Task Identity_lookup_normalizes_whitespace_and_unicode_but_preserves_case_and_author_and_ignores_hash()
    {
        using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ImportAsync("Fixture.txt", "same body", "Café Book");
        var repeated = await fixture.ImportAsync("Other.txt", "changed body", " Cafe\u0301\u2003Book ");
        Assert.Equal(first.ImportedBook!.BookId, repeated.ImportedBook!.BookId);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM Books;"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM BookSourceBindings;"));
        Assert.Equal("Café Book", repeated.ImportedBook.Title);
        Assert.Null(await fixture.Repository.FindByIdentityAsync(BookIdentity.Create("café Book", ""), CancellationToken.None));
        Assert.Null(await fixture.Repository.FindByIdentityAsync(BookIdentity.Create("Café Book", "someone"), CancellationToken.None));
        Assert.NotNull(await fixture.Repository.FindByIdentityAsync(BookIdentity.Create("Café  Book", null), CancellationToken.None));
        var other = await fixture.ImportAsync("Other.txt", "changed body", "Other");
        Assert.NotEqual(first.ImportedBook.BookId, other.ImportedBook?.BookId);
    }

    [Fact]
    public async Task Failed_catalog_commit_preserves_old_content_metadata_and_catalog()
    {
        using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ImportAsync("Fixture.txt", "old body");
        var old = (await fixture.Repository.GetTargetAsync(first.ImportedBook!.BookId, CancellationToken.None))!;
        var chapter = await fixture.StringScalarAsync("SELECT Id FROM Chapters;");
        await fixture.ExecuteAsync("CREATE TRIGGER RejectCatalog BEFORE INSERT ON LocalChapterContents BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, change) => changes.Add(change);
        await Assert.ThrowsAsync<SqliteException>(() => fixture.ImportAsync("Renamed.txt", "replacement body", "Fixture"));
        var after = (await fixture.Repository.GetTargetAsync(old.Book.Id, CancellationToken.None))!;
        Assert.Equal(old, after);
        Assert.Equal(chapter, await fixture.StringScalarAsync("SELECT Id FROM Chapters;"));
        Assert.Equal("old body", await fixture.ReadContentAsync(after));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Directories.BooksDirectoryPath, old.Book.Id)));
        Assert.Empty(changes);
    }

    [Fact]
    public async Task Binding_local_source_to_existing_book_does_not_activate_it()
    {
        using var fixture = await Fixture.CreateAsync();
        var now = DateTimeOffset.UnixEpoch.ToString("O");
        await fixture.ExecuteAsync($"INSERT INTO Books(Id, Title, Author, NormalizedTitle, NormalizedAuthor, ImportedAt, UpdatedAt) VALUES('existing', 'Displayed', '', 'Displayed', '', '{now}', '{now}');");
        var changes = new List<BookCommittedChange>();
        fixture.Changes.Changed += (_, change) => changes.Add(change);
        var result = await fixture.ImportAsync("Fixture.txt", "body", "Displayed");
        var target = (await fixture.Repository.GetTargetAsync("existing", CancellationToken.None))!;
        Assert.Equal("existing", result.ImportedBook?.BookId);
        Assert.Null(target.Book.ActiveSourceBindingId);
        Assert.Equal("Displayed", target.Book.Title);
        Assert.NotNull(target.LocalBinding);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM Chapters;"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM LocalChapterContents;"));
        Assert.Empty(changes);
    }

    [Fact]
    public async Task Stale_prepared_snapshot_cannot_overwrite_a_newer_local_source()
    {
        using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ImportAsync("Fixture.txt", "old body");
        var old = (await fixture.Repository.GetTargetAsync(first.ImportedBook!.BookId, CancellationToken.None))!;
        await fixture.ImportAsync("Fixture.txt", "new body");
        var current = (await fixture.Repository.GetTargetAsync(old.Book.Id, CancellationToken.None))!;
        var snapshot = new LocalSourceImportSnapshot(old.Book, old.Binding!, old.LocalBinding!,
            new CurrentCatalog(old.Book.Id, old.Binding!.BindingId,
                [new("prepared-chapter", old.Book.Id, old.Binding.BindingId, 0, 0, "Fixture")]), [new("prepared-chapter", 0, 1)],
            false, old.LocalBinding!.StoredContentPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.SaveAsync(snapshot, "stale-operation", CancellationToken.None));
        Assert.Equal(current, await fixture.Repository.GetTargetAsync(old.Book.Id, CancellationToken.None));
        Assert.Equal("new body", await fixture.ReadContentAsync(current));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Recovery_keeps_the_referenced_snapshot_and_cleans_only_unreferenced_files(bool committed, bool absoluteJournalPaths)
    {
        using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ImportAsync("Fixture.txt", "old body");
        var old = (await fixture.Repository.GetTargetAsync(first.ImportedBook!.BookId, CancellationToken.None))!;
        var store = new BookFileStore(fixture.Directories, fixture.Resolver);
        var staged = await store.StageNormalizedTextAsync("new body", old.Book.Id, null, CancellationToken.None);
        var operationId = Guid.NewGuid().ToString("N");
        var journal = new SqliteBookOperationJournal(fixture.Factory, TimeProvider.System);
        string JournalPath(string key) => absoluteJournalPaths ? fixture.Resolver.ResolvePath(key) : key;
        await journal.CreateAsync(new BookOperationRecord(operationId, BookOperationKind.Import, BookOperationPhase.Staged, old.Book.Id,
            [new(JournalPath(staged.FinalPath), JournalPath(staged.TemporaryPath), false),
                new(JournalPath(old.LocalBinding!.StoredContentPath), JournalPath(old.LocalBinding.StoredContentPath), false)],
            DateTimeOffset.UtcNow), CancellationToken.None);
        await store.FinalizeAsync(staged, CancellationToken.None);
        if (committed)
        {
            await fixture.ExecuteAsync($"UPDATE LocalBookSourceBindings SET StoredContentPath = '{staged.FinalPath}';");
        }

        var recovery = new BookOperationRecoveryService(fixture.Factory, journal, fixture.Resolver, fixture.Directories);
        await recovery.RecoverAsync(CancellationToken.None);
        await recovery.RecoverAsync(CancellationToken.None);
        Assert.Equal(committed, File.Exists(fixture.Resolver.ResolvePath(staged.FinalPath)));
        Assert.Equal(!committed, File.Exists(fixture.Resolver.ResolvePath(old.LocalBinding.StoredContentPath)));
        Assert.Empty(await journal.GetIncompleteAsync(CancellationToken.None));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM Books;"));
    }

    [Fact]
    public async Task Recovery_rejects_a_staging_path_that_aliases_the_committed_content()
    {
        using var fixture = await Fixture.CreateAsync();
        var imported = await fixture.ImportAsync("Fixture.txt", "owned body");
        var target = (await fixture.Repository.GetTargetAsync(imported.ImportedBook!.BookId, CancellationToken.None))!;
        var journal = new SqliteBookOperationJournal(fixture.Factory, TimeProvider.System);
        var path = target.LocalBinding!.StoredContentPath;
        await journal.CreateAsync(new BookOperationRecord("invalid-operation", BookOperationKind.Import, BookOperationPhase.Staged,
            target.Book.Id, [new(path, path, false)], DateTimeOffset.UtcNow), CancellationToken.None);
        var recovery = new BookOperationRecoveryService(fixture.Factory, journal, fixture.Resolver, fixture.Directories);
        await Assert.ThrowsAsync<InvalidDataException>(() => recovery.RecoverAsync(CancellationToken.None));
        Assert.Equal("owned body", await fixture.ReadContentAsync(target));
        Assert.Equal(target, await fixture.Repository.GetTargetAsync(target.Book.Id, CancellationToken.None));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        public AppDataDirectoryProvider Directories { get; }
        public AppStoragePathResolver Resolver { get; }
        public SqliteConnectionFactory Factory { get; }
        public BookImportRepository Repository { get; }
        public SqliteBookOperationJournal Journal { get; }
        public BookDeletionService Deletion { get; }
        public BookSourceChanges Changes { get; } = new();
        private BookMutationGate Mutations { get; } = new();
        private DirectBookImportService Service { get; }

        private Fixture(ITextFileAnalyzer? analyzer)
        {
            Directories = new AppDataDirectoryProvider(Path.Combine(Root, "data"));
            Resolver = new AppStoragePathResolver(Directories);
            Factory = new SqliteConnectionFactory(Directories, observability: null, pooling: false);
            Repository = new BookImportRepository(Factory);
            Journal = new SqliteBookOperationJournal(Factory, TimeProvider.System);
            Deletion = new BookDeletionService(new BookDeletionOperationStore(Factory, Directories,
                new AudioCacheProtectionRegistry(), Resolver, Journal, TimeProvider.System), Mutations, Changes, [], []);
            Service = new DirectBookImportService(analyzer ?? new TextFileAnalyzer(), new TextNormalizer(), new Sha256ContentHasher(),
                new ChapterRuleRepository(Factory), new ChapterSplitter(), new BookFileStore(Directories, Resolver), Repository,
                Journal, new FileNameMetadataRuleRepository(Factory),
                new TextHeaderMetadataRuleRepository(Factory), new Settings(), new ImportMetadataExtractor(), TimeProvider.System, new Ids(), Mutations, Changes);
        }

        public static async Task<Fixture> CreateAsync(ITextFileAnalyzer? analyzer = null)
        {
            var fixture = new Fixture(analyzer);
            await fixture.Directories.EnsureCreatedAsync(CancellationToken.None);
            await new SqliteMigrationRunner(fixture.Factory).InitializeAsync(CancellationToken.None);
            return fixture;
        }

        public async Task<DirectBookImportResult> ImportAsync(string name, string text, string? title = null)
        {
            var path = Path.Combine(Root, name);
            await File.WriteAllTextAsync(path, text);
            return await Service.ImportAsync(new DirectBookImportRequest(path, "utf-8", name, new(title ?? Path.GetFileNameWithoutExtension(name), "")), null, CancellationToken.None);
        }

        public Task<string> ReadContentAsync(LocalSourceImportTarget target) => File.ReadAllTextAsync(Resolver.ResolvePath(target.LocalBinding!.StoredContentPath));
        public Task<DirectBookImportResult> ImportRequestAsync(DirectBookImportRequest request, CancellationToken cancellationToken) =>
            Service.ImportAsync(request, null, cancellationToken);
        public async Task ExecuteAsync(string sql)
        {
            await using var connection = await Factory.OpenConnectionAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        public async Task<long> ScalarAsync(string sql) => Convert.ToInt64(await StringScalarAsync(sql));
        public async Task<string> StringScalarAsync(string sql)
        {
            await using var connection = await Factory.OpenConnectionAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(await command.ExecuteScalarAsync())!;
        }
        public void Dispose()
        {
            Mutations.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class PausingAnalyzer : ITextFileAnalyzer
    {
        private readonly TextFileAnalyzer _inner = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TextFileAnalysis> AnalyzeAsync(BookImportRequest request, IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            var analysis = await _inner.AnalyzeAsync(request, progress, cancellationToken);
            if (Path.GetFileName(request.FilePath) == "Paused.txt")
            {
                Started.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return analysis;
        }
    }

    private sealed class Ids : IBookImportIdGenerator
    {
        public string CreateBookId() => Guid.NewGuid().ToString("N");
        public string CreateSourceId() => Guid.NewGuid().ToString("N");
        public string CreateChapterId() => Guid.NewGuid().ToString("N");
        public string CreateOperationId() => Guid.NewGuid().ToString("N");
    }

    private sealed class Settings : IAppSettingsService
    {
        public AppSettings Current => AppSettings.Default;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }
        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) => Task.FromResult(Current);
    }
}
