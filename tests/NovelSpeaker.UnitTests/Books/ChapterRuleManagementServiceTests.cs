using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class ChapterRuleManagementServiceTests
{
    [Fact]
    public async Task PreviewDefaultsAsync_reports_added_updated_and_unchanged_rules()
    {
        var (repository, service) = await CreateServiceAsync();
        var existing = await repository.GetAllAsync(CancellationToken.None);
        await repository.DeleteAsync("builtin:chapter-epilogue", CancellationToken.None);
        await repository.SaveAsync(existing.Single(rule => rule.Id == "builtin:chapter-number") with
        {
            Pattern = @"^\s*变更后的正则$",
            UpdatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        var preview = await service.PreviewDefaultsAsync(ChapterRuleDefaultsMode.ImportDefaults, CancellationToken.None);

        Assert.Equal(1, preview.AddedCount);
        Assert.Equal(1, preview.UpdatedCount);
        Assert.Equal(2, preview.UnchangedCount);
    }

    [Fact]
    public async Task ApplyDefaultsAsync_restore_defaults_resets_builtin_rules_and_keeps_custom_rules()
    {
        var (repository, service) = await CreateServiceAsync();
        var builtIn = (await repository.GetAllAsync(CancellationToken.None)).Single(rule => rule.Id == "builtin:chapter-number");
        await repository.SaveAsync(builtIn with
        {
            Pattern = @"^\s*已被修改$",
            SortOrder = 999,
            IsEnabled = false,
            UpdatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        await repository.SaveAsync(new ChapterRule(
            "custom:demo",
            "自定义规则",
            @"^\s*自定义$",
            500,
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow), CancellationToken.None);

        var result = await service.ApplyDefaultsAsync(ChapterRuleDefaultsMode.RestoreDefaults, CancellationToken.None);
        var allRules = await repository.GetAllAsync(CancellationToken.None);
        var restored = allRules.Single(rule => rule.Id == "builtin:chapter-number");
        var custom = allRules.Single(rule => rule.Id == "custom:demo");

        Assert.True(result.UpdatedCount >= 1);
        Assert.Equal(@"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", restored.Pattern);
        Assert.Equal(10, restored.SortOrder);
        Assert.True(restored.IsEnabled);
        Assert.Equal(@"^\s*自定义$", custom.Pattern);
    }

    private static async Task<(ChapterRuleRepository Repository, ChapterRuleManagementService Service)> CreateServiceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);
        await initializer.InitializeAsync(CancellationToken.None);
        return (repository, new ChapterRuleManagementService(repository));
    }
}
