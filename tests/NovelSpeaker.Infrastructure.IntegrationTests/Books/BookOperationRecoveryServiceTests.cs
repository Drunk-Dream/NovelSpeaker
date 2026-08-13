using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class BookOperationRecoveryServiceTests
{
    [Fact]
    public async Task RecoverAsync_discards_uncommitted_import_and_is_idempotent()
    {
        var fixture = await CreateFixtureAsync();
        var stagedPath = Path.Combine(fixture.Directories.BooksDirectoryPath, "book-1", "content.txt.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        await File.WriteAllTextAsync(stagedPath, "正文", CancellationToken.None);
        await fixture.Journal.CreateAsync(CreateImport("operation-1", "book-1", BookOperationPhase.Staged), CancellationToken.None);

        await fixture.Recovery.RecoverAsync(CancellationToken.None);
        await fixture.Recovery.RecoverAsync(CancellationToken.None);

        Assert.False(File.Exists(stagedPath));
        Assert.Empty(await fixture.Journal.GetIncompleteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecoverAsync_finalizes_import_when_database_row_exists()
    {
        foreach (var phase in new[] { BookOperationPhase.Staged, BookOperationPhase.DatabaseCommitted })
        {
            var fixture = await CreateFixtureAsync();
            await SeedBookAsync(fixture, "book-1", "Books/book-1/content.txt");
            var stagedPath = Path.Combine(fixture.Directories.BooksDirectoryPath, "book-1", "content.txt.tmp");
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            await File.WriteAllTextAsync(stagedPath, "正文", CancellationToken.None);
            await fixture.Journal.CreateAsync(CreateImport("operation-1", "book-1", phase), CancellationToken.None);

            await fixture.Recovery.RecoverAsync(CancellationToken.None);
            await fixture.Recovery.RecoverAsync(CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(fixture.Directories.BooksDirectoryPath, "book-1", "content.txt")));
            Assert.True(await BookExistsAsync(fixture, "book-1"));
        }
    }

    [Fact]
    public async Task RecoverAsync_rolls_back_database_when_committed_import_has_no_content_file()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", "Books/book-1/content.txt");
        await fixture.Journal.CreateAsync(
            CreateImport("operation-1", "book-1", BookOperationPhase.DatabaseCommitted),
            CancellationToken.None);

        await fixture.Recovery.RecoverAsync(CancellationToken.None);

        Assert.False(await BookExistsAsync(fixture, "book-1"));
        Assert.Empty(await fixture.Journal.GetIncompleteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecoverAsync_restores_staged_delete_when_database_still_owns_book()
    {
        var fixture = await CreateFixtureAsync();
        await SeedBookAsync(fixture, "book-1", "Books/book-1/content.txt");
        var originalDirectory = Path.Combine(fixture.Directories.BooksDirectoryPath, "book-1");
        Directory.CreateDirectory(originalDirectory);
        await File.WriteAllTextAsync(Path.Combine(originalDirectory, "content.txt"), "正文", CancellationToken.None);
        var stagedDirectory = Path.Combine(fixture.Directories.OperationsDirectoryPath, "operation-1", "book");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedDirectory)!);
        Directory.Move(originalDirectory, stagedDirectory);
        await fixture.Journal.CreateAsync(
            CreateDelete("operation-1", "book-1", BookOperationPhase.Staged),
            CancellationToken.None);

        await fixture.Recovery.RecoverAsync(CancellationToken.None);
        await fixture.Recovery.RecoverAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(originalDirectory, "content.txt")));
        Assert.True(await BookExistsAsync(fixture, "book-1"));
    }

    [Fact]
    public async Task RecoverAsync_removes_all_remaining_staged_delete_files_after_database_commit()
    {
        var fixture = await CreateFixtureAsync();
        var stageRoot = Path.Combine(fixture.Directories.OperationsDirectoryPath, "operation-1");
        var stagedBook = Path.Combine(stageRoot, "book");
        var stagedCache = Path.Combine(stageRoot, "cache", "00000000.mp3");
        Directory.CreateDirectory(stagedBook);
        Directory.CreateDirectory(Path.GetDirectoryName(stagedCache)!);
        await File.WriteAllTextAsync(Path.Combine(stagedBook, "content.txt"), "正文", CancellationToken.None);
        await File.WriteAllTextAsync(stagedCache, "cache", CancellationToken.None);
        await fixture.Journal.CreateAsync(
            new BookOperationRecord(
                "operation-1",
                BookOperationKind.Delete,
                BookOperationPhase.DatabaseCommitted,
                "book-1",
                [
                    new("Books/book-1", "Operations/operation-1/book", true),
                    new("Cache/Tts/v1/a/cache.mp3", "Operations/operation-1/cache/00000000.mp3", false),
                    new("Cache/Tts/v1/a/missing.mp3", "Operations/operation-1/cache/00000001.mp3", false)
                ],
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await fixture.Recovery.RecoverAsync(CancellationToken.None);
        await fixture.Recovery.RecoverAsync(CancellationToken.None);

        Assert.False(Directory.Exists(stagedBook));
        Assert.False(File.Exists(stagedCache));
    }

    [Fact]
    public async Task RecoverAsync_rejects_tampered_path_without_touching_external_file()
    {
        var fixture = await CreateFixtureAsync();
        var externalPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.txt");
        await File.WriteAllTextAsync(externalPath, "external", CancellationToken.None);
        await fixture.Journal.CreateAsync(
            new BookOperationRecord(
                "operation-1",
                BookOperationKind.Import,
                BookOperationPhase.Staged,
                "book-1",
                [new(externalPath, "Books/book-1/content.txt.tmp", false)],
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Recovery.RecoverAsync(CancellationToken.None));

        Assert.True(File.Exists(externalPath));
        Assert.Equal("external", await File.ReadAllTextAsync(externalPath, CancellationToken.None));
    }

    [Fact]
    public async Task RecoverAsync_rejects_tampered_delete_path_without_touching_external_file()
    {
        var fixture = await CreateFixtureAsync();
        var externalPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.txt");
        await File.WriteAllTextAsync(externalPath, "external", CancellationToken.None);
        await fixture.Journal.CreateAsync(
            new BookOperationRecord(
                "operation-1",
                BookOperationKind.Delete,
                BookOperationPhase.DatabaseCommitted,
                "book-1",
                [new(externalPath, "Operations/operation-1/cache/item.txt", false)],
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Recovery.RecoverAsync(CancellationToken.None));
            Assert.True(File.Exists(externalPath));
            Assert.NotEmpty(await fixture.Journal.GetIncompleteAsync(CancellationToken.None));
        }
        finally
        {
            File.Delete(externalPath);
        }
    }

    private static BookOperationRecord CreateImport(string operationId, string bookId, BookOperationPhase phase) =>
        new(
            operationId,
            BookOperationKind.Import,
            phase,
            bookId,
            [new($"Books/{bookId}/content.txt", $"Books/{bookId}/content.txt.tmp", false)],
            DateTimeOffset.UtcNow);

    private static BookOperationRecord CreateDelete(string operationId, string bookId, BookOperationPhase phase) =>
        new(
            operationId,
            BookOperationKind.Delete,
            phase,
            bookId,
            [new($"Books/{bookId}", $"Operations/{operationId}/book", true)],
            DateTimeOffset.UtcNow);

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var directories = new LocalAppDataDirectoryProvider(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var factory = new SqliteConnectionFactory(directories);
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        var journal = new SqliteBookOperationJournal(factory, TimeProvider.System);
        var resolver = new AppStoragePathResolver(directories);
        var recovery = new BookOperationRecoveryService(factory, journal, resolver, directories);
        return new TestFixture(directories, factory, journal, recovery);
    }

    private static async Task SeedBookAsync(TestFixture fixture, string bookId, string storedFilePath)
    {
        await using var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Books
                (Id, Title, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, UpdatedAt)
            VALUES
                ($id, 'book', 'external.txt', $storedFilePath, $hash, 'utf-8', $now, $now);
            """;
        command.Parameters.AddWithValue("$id", bookId);
        command.Parameters.AddWithValue("$storedFilePath", storedFilePath);
        command.Parameters.AddWithValue("$hash", $"hash-{bookId}");
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<bool> BookExistsAsync(TestFixture fixture, string bookId)
    {
        await using var connection = await fixture.Factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Books WHERE Id = $id);";
        command.Parameters.AddWithValue("$id", bookId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None)) == 1;
    }

    private sealed record TestFixture(
        LocalAppDataDirectoryProvider Directories,
        SqliteConnectionFactory Factory,
        SqliteBookOperationJournal Journal,
        BookOperationRecoveryService Recovery);
}
