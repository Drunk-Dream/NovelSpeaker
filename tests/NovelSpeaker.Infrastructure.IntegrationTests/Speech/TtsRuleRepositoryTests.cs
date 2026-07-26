using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Speech.Rules;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class TtsRuleRepositoryTests
{
    [Fact]
    public async Task SaveAsync_persists_rule_and_roundtrips_metadata()
    {
        var repository = await CreateRepositoryAsync();
        var utcNow = DateTime.UtcNow.ToString("O");

        var ruleId = await repository.SaveAsync(TestHttpTtsRules.Create(
            0,
            "示例规则",
            "https://example.com/tts?text={{speakText}}",
            "audio/mpeg",
            "2/1000",
            "{\"Authorization\":\"Bearer demo\"}",
            """{"method":"POST","body":"{\"text\":\"{{speakText}}\"}"}""",
            12345,
            true,
            utcNow,
            utcNow,
            utcNow), CancellationToken.None);

        var stored = await repository.GetByIdAsync(ruleId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("示例规则", stored!.Name);
        Assert.Equal("https://example.com/tts?text={{speakText}}", stored.Url);
        Assert.Equal(12345, stored.LastUpdateTime);
        Assert.Equal(DateTimeOffset.Parse(utcNow), stored.LastUsedAt);
        Assert.Equal("POST", stored.RequestMethod);
        Assert.Equal("""{"text":"{{speakText}}"}""", stored.RequestBody);
        Assert.False(stored.RequestBodyIsJsonStructure);
    }

    [Fact]
    public async Task SaveAsync_updates_existing_rule()
    {
        var repository = await CreateRepositoryAsync();
        var utcNow = DateTime.UtcNow.ToString("O");
        var ruleId = await repository.SaveAsync(TestHttpTtsRules.Create(
            0,
            "规则 A",
            "https://example.com/a",
            null,
            null,
            null,
            null,
            null,
            true,
            null,
            utcNow,
            utcNow), CancellationToken.None);

        await repository.SaveAsync(TestHttpTtsRules.Create(
            ruleId,
            "规则 A 已更新",
            "https://example.com/a2",
            null,
            null,
            null,
            """{"method":"POST"}""",
            null,
            false,
            utcNow,
            utcNow,
            utcNow), CancellationToken.None);

        var stored = await repository.GetByIdAsync(ruleId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("规则 A 已更新", stored!.Name);
        Assert.Equal("https://example.com/a2", stored.Url);
        Assert.False(stored.IsEnabled);
        Assert.Equal("POST", stored.RequestMethod);
        Assert.Null(stored.RequestBody);
    }

    [Fact]
    public async Task DeleteAsync_removes_rule()
    {
        var repository = await CreateRepositoryAsync();
        var utcNow = DateTime.UtcNow.ToString("O");
        var ruleId = await repository.SaveAsync(TestHttpTtsRules.Create(
            0,
            "待删除规则",
            "https://example.com/delete",
            null,
            null,
            null,
            null,
            null,
            true,
            null,
            utcNow,
            utcNow), CancellationToken.None);

        await repository.DeleteAsync(ruleId, CancellationToken.None);

        Assert.Null(await repository.GetByIdAsync(ruleId, CancellationToken.None));
    }

    [Fact]
    public async Task ExportRuleJson_roundtrips_from_structured_columns()
    {
        var repository = await CreateRepositoryAsync();
        var utcNow = DateTime.UtcNow.ToString("O");

        var ruleId = await repository.SaveAsync(TestHttpTtsRules.Create(
            0,
            "导出规则",
            "https://example.com/export",
            "audio/mpeg",
            "2/1000",
            """{"Authorization":"Bearer demo"}""",
            """{"method":"POST","body":"{\"text\":\"{{speakText}}\"}"}""",
            123,
            true,
            null,
            utcNow,
            utcNow), CancellationToken.None);
        var stored = (await repository.GetByIdAsync(ruleId, CancellationToken.None))!;

        Assert.Equal("Bearer demo", stored.Headers["Authorization"]);
        Assert.Equal("POST", stored.RequestMethod);
        Assert.Equal("{\"text\":\"{{speakText}}\"}", stored.RequestBody);
    }

    [Fact]
    public async Task SaveAsync_preserves_structured_json_body_shape()
    {
        var repository = await CreateRepositoryAsync();
        var utcNow = DateTimeOffset.UtcNow.ToString("O");
        var ruleId = await repository.SaveAsync(TestHttpTtsRules.Create(
            0,
            "结构化 Body",
            "https://example.com/tts",
            "audio/mpeg",
            null,
            null,
            """{"method":"POST","body":{"text":"{{speakText}}"}}""",
            null,
            true,
            null,
            utcNow,
            utcNow), CancellationToken.None);

        var stored = (await repository.GetByIdAsync(ruleId, CancellationToken.None))!;

        Assert.Equal("""{"text":"{{speakText}}"}""", stored.RequestBody);
        Assert.True(stored.RequestBodyIsJsonStructure);
    }

    [Fact]
    public async Task GetAllAsync_accepts_legacy_times_and_skips_damaged_history()
    {
        var (factory, repository) = await CreateRepositoryFixtureAsync();
        await using (var connection = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO HttpTtsRules
                    (Name, Url, IsEnabled, CreatedAt, UpdatedAt)
                VALUES
                    ('旧时间', 'https://example.com/legacy', 1, '2026-07-16 09:08:07', '2026-07-16T09:09:08Z'),
                    ('损坏时间', 'https://example.com/damaged', 1, 'not-a-date', 'also-invalid');
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var rules = await repository.GetAllAsync(CancellationToken.None);

        var legacy = Assert.Single(rules);
        Assert.Equal("旧时间", legacy.Name);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 16, 9, 8, 7, TimeSpan.Zero),
            legacy.CreatedAt);
    }

    private static async Task<TtsRuleRepository> CreateRepositoryAsync()
    {
        return (await CreateRepositoryFixtureAsync()).Repository;
    }

    private static async Task<(SqliteConnectionFactory Factory, TtsRuleRepository Repository)>
        CreateRepositoryFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var seederRepository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(seederRepository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);

        await initializer.InitializeAsync(CancellationToken.None);
        return (factory, new TtsRuleRepository(factory));
    }
}
