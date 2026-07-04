using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class ChapterRuleRepositoryTests
{
    [Fact]
    public async Task ImportDefaultsAsync_skips_exact_duplicates_and_preserves_existing_rows()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
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
            DateTime.UtcNow.ToString("O"),
            DateTime.UtcNow.ToString("O"));

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
        var directories = new LocalAppDataDirectoryProvider(root);
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
            DateTime.UtcNow.ToString("O"),
            DateTime.UtcNow.ToString("O")), CancellationToken.None);
        await repository.SaveAsync(new ChapterRule(
            "custom:two",
            "自定义二",
            @"^\s*二$",
            200,
            true,
            DateTime.UtcNow.ToString("O"),
            DateTime.UtcNow.ToString("O")), CancellationToken.None);

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
}
