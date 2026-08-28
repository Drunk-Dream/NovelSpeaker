using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Playback;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Persistence;

public sealed class SqliteChapterSpeechPlanStoreTests
{
    [Fact]
    public async Task SaveAsync_updates_only_the_header_when_plan_output_is_unchanged()
    {
        var (factory, _) = await CreateDatabaseAsync();
        var store = new SqliteChapterSpeechPlanStore(factory);
        var first = CreatePlan(
            TextProfileFingerprint.Create(TextSegmentationOptions.Default, []),
            [CreateSegment(0, 0, 4, "第一段")]);
        var second = first with
        {
            ChapterRevisionHash = Fingerprint.Sha256("new chapter revision"),
            TextProfileFingerprint = TextProfileFingerprint.Create(
                new TextSegmentationOptions(false, 300),
                [])
        };

        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(second, CancellationToken.None);

        var loaded = await store.GetAsync("chapter-1", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(second.TextProfileFingerprint, loaded!.TextProfileFingerprint);
        Assert.Equal(second.ChapterRevisionHash, loaded.ChapterRevisionHash);
        Assert.Equal(first.PlanOutputHash, loaded.PlanOutputHash);
        Assert.Equal(first.Segments, loaded.Segments);
    }

    [Fact]
    public async Task SaveAsync_replaces_segments_in_one_transaction_when_output_changes()
    {
        var (factory, _) = await CreateDatabaseAsync();
        var store = new SqliteChapterSpeechPlanStore(factory);
        var first = CreatePlan(
            TextProfileFingerprint.Create(TextSegmentationOptions.Default, []),
            [CreateSegment(0, 0, 4, "第一段")]);
        var second = first with
        {
            PlanOutputHash = Fingerprint.Sha256("different plan output"),
            BodySegmentCount = 2,
            Segments =
            [
                CreateSegment(0, 0, 4, "第一段"),
                CreateSegment(1, 4, 4, "第二段")
            ]
        };

        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(second, CancellationToken.None);

        var loaded = await store.GetAsync("chapter-1", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.BodySegmentCount);
        Assert.Equal([0, 1], loaded.Segments.Select(segment => segment.OrderIndex));
    }

    [Fact]
    public async Task SaveAsync_rolls_back_header_when_segment_replacement_fails()
    {
        var (factory, _) = await CreateDatabaseAsync();
        var store = new SqliteChapterSpeechPlanStore(factory);
        var invalid = CreatePlan(
            TextProfileFingerprint.Create(TextSegmentationOptions.Default, []),
            [
                CreateSegment(0, 0, 4, "第一段"),
                CreateSegment(1, 0, 4, "重复来源")
            ]);

        await Assert.ThrowsAsync<SqliteException>(() => store.SaveAsync(invalid, CancellationToken.None));

        Assert.Null(await store.GetAsync("chapter-1", CancellationToken.None));
    }

    private static ChapterSpeechPlan CreatePlan(
        TextProfileFingerprint textProfile,
        IReadOnlyList<ChapterSpeechPlanSegment> segments) =>
        new(
            "chapter-1",
            Fingerprint.Sha256("chapter revision"),
            textProfile,
            Fingerprint.Sha256(string.Join('|', segments.Select(segment => segment.SpeechTextHash.Hex))),
            ChapterSpeechPlanState.Ready,
            segments.Count,
            DateTimeOffset.UtcNow,
            segments);

    private static ChapterSpeechPlanSegment CreateSegment(
        int order,
        int start,
        int length,
        string speechText) =>
        new(
            order,
            SpeechSegmentKind.Body,
            start,
            length,
            Fingerprint.Sha256(speechText));

    private static async Task<(SqliteConnectionFactory Factory, AppDataDirectoryProvider Directories)> CreateDatabaseAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        await new StartupDatabaseInitializer(directories, runner, seeder).InitializeAsync(CancellationToken.None);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Books
                (Id, Title, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, UpdatedAt)
            VALUES
                ('book-1', '书', 'book.txt', 'Books/book-1/content.txt', 'plan-fixture', 'utf-8', '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
            INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, StartOffset, Length)
            VALUES ('chapter-1', 'book-1', 0, 0, '第一章', 0, 8);
            """;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return (factory, directories);
    }
}
