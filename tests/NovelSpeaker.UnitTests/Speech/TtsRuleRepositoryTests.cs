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

        var exportedJson = NovelSpeakerRuleJsonSerializer.Serialize(stored);

        Assert.Equal(
            """{"name":"导出规则","url":"https://example.com/export","contentType":"audio/mpeg","concurrentRate":"2/1000","header":"{\"Authorization\":\"Bearer demo\"}","requestOptions":{"method":"POST","body":"{\"text\":\"{{speakText}}\"}"},"lastUpdateTime":123}""",
            exportedJson);
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
        Assert.Equal(
            """{"name":"结构化 Body","url":"https://example.com/tts","contentType":"audio/mpeg","requestOptions":{"method":"POST","body":{"text":"{{speakText}}"}}}""",
            NovelSpeakerRuleJsonSerializer.Serialize(stored));
    }

    private static async Task<TtsRuleRepository> CreateRepositoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var seederRepository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(seederRepository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);

        await initializer.InitializeAsync(CancellationToken.None);
        return new TtsRuleRepository(factory);
    }
}
