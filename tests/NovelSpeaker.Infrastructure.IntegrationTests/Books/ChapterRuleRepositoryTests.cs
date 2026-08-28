using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class ChapterRuleRepositoryTests
{
    [Fact]
    public async Task ImportDefaultsAsync_skips_exact_duplicates_and_preserves_existing_rows()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var seederRepository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(seederRepository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);

        await initializer.InitializeAsync(CancellationToken.None);

        var repository = new ChapterRuleRepository(factory);
        var existing = new ChapterRule(
            Guid.NewGuid().ToString(),
            "章节数字",
            @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$",
            90,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await repository.SaveAsync(existing, CancellationToken.None);

        var insertedCount = await repository.ImportDefaultsAsync(CancellationToken.None);
        var allRules = await repository.GetAllAsync(CancellationToken.None);
        var preserved = allRules.Single(rule => rule.Id == existing.Id);

        Assert.Equal(0, insertedCount);
        Assert.False(preserved.IsEnabled);
        Assert.Equal(2, allRules.Count(rule => rule.Name == existing.Name && rule.Pattern == existing.Pattern));
    }

    [Fact]
    public async Task SaveOrderAsync_updates_multiple_rule_sort_orders()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var seederRepository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(seederRepository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);

        await initializer.InitializeAsync(CancellationToken.None);

        var repository = new ChapterRuleRepository(factory);
        await repository.SaveAsync(new ChapterRule(
            "custom:one",
            "自定义一",
            @"^\s*一$",
            100,
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow), CancellationToken.None);
        await repository.SaveAsync(new ChapterRule(
            "custom:two",
            "自定义二",
            @"^\s*二$",
            200,
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow), CancellationToken.None);

        await repository.SaveOrderAsync(
        [
            ("custom:two", 10),
            ("builtin:chapter-number", 20),
            ("custom:one", 30),
            ("builtin:chapter-volume", 40),
            ("builtin:chapter-preface", 50),
            ("builtin:chapter-epilogue", 60)
        ], CancellationToken.None);

        var orderedIds = (await repository.GetAllAsync(CancellationToken.None))
            .Select(rule => rule.Id)
            .ToArray();

        Assert.Equal("custom:two", orderedIds[0]);
        Assert.Equal("builtin:chapter-number", orderedIds[1]);
        Assert.Equal("custom:one", orderedIds[2]);
    }

    [Fact]
    public async Task GetAllAsync_accepts_legacy_utc_times_and_skips_damaged_history()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var seederRepository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(seederRepository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);

        await using (var connection = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO ChapterRules (Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt)
                VALUES
                    ('legacy-time', '旧时间', '^legacy$', 100, 1, '2026-07-16 09:08:07', '2026-07-16T09:09:08Z'),
                    ('damaged-time', '损坏时间', '^damaged$', 101, 1, 'not-a-date', 'also-invalid');
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var rules = await new ChapterRuleRepository(factory).GetAllAsync(CancellationToken.None);

        var legacy = Assert.Single(rules, rule => rule.Id == "legacy-time");
        Assert.Equal(
            new DateTimeOffset(2026, 7, 16, 9, 8, 7, TimeSpan.Zero),
            legacy.CreatedAt);
        Assert.DoesNotContain(rules, rule => rule.Id == "damaged-time");
    }
}
