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

        var ruleId = await repository.SaveAsync(new HttpTtsRule(
            0,
            "示例规则",
            "https://example.com/tts?text={{speakText}}",
            "audio/mpeg",
            "2/1000",
            "{\"Authorization\":\"Bearer demo\"}",
            """{"method":"POST","body":"{\"text\":\"{{speakText}}\"}"}""",
            false,
            12345,
            """
            {"name":"示例规则","url":"https://example.com/tts?text={{speakText}}","contentType":"audio/mpeg","concurrentRate":"2/1000","header":"{\"Authorization\":\"Bearer demo\"}","requestOptions":{"method":"POST","body":"{\"text\":\"{{speakText}}\"}"},"lastUpdateTime":12345}
            """,
            true,
            TtsRuleCompatibilityStatus.CompatibleWithWarnings,
            ["loginUrl"],
            utcNow,
            utcNow,
            utcNow)
        {
            LoginInfoJson = """{"token":"secret-token"}"""
        }, CancellationToken.None);

        var stored = await repository.GetByIdAsync(ruleId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("示例规则", stored!.Name);
        Assert.Equal("https://example.com/tts?text={{speakText}}", stored.Url);
        Assert.Equal(TtsRuleCompatibilityStatus.CompatibleWithWarnings, stored.CompatibilityStatus);
        Assert.Equal(["loginUrl"], stored.UnsupportedFields);
        Assert.Equal(12345, stored.LastUpdateTime);
        Assert.Equal(utcNow, stored.LastUsedAt);
        Assert.Equal("""{"method":"POST","body":"{\"text\":\"{{speakText}}\"}"}""", stored.RequestOptionsJson);
        Assert.Equal("""{"token":"secret-token"}""", stored.LoginInfoJson);
    }

    [Fact]
    public async Task SaveAsync_updates_existing_rule()
    {
        var repository = await CreateRepositoryAsync();
        var utcNow = DateTime.UtcNow.ToString("O");
        var ruleId = await repository.SaveAsync(new HttpTtsRule(
            0,
            "规则 A",
            "https://example.com/a",
            null,
            null,
            null,
            null,
            false,
            null,
            """{"name":"规则 A","url":"https://example.com/a"}""",
            true,
            TtsRuleCompatibilityStatus.Compatible,
            [],
            null,
            utcNow,
            utcNow), CancellationToken.None);

        await repository.SaveAsync(new HttpTtsRule(
            ruleId,
            "规则 A 已更新",
            "https://example.com/a2",
            null,
            null,
            null,
            """{"method":"POST"}""",
            false,
            null,
            """{"name":"规则 A 已更新","url":"https://example.com/a2","requestOptions":{"method":"POST"}}""",
            false,
            TtsRuleCompatibilityStatus.CompatibleWithWarnings,
            ["customField"],
            utcNow,
            utcNow,
            utcNow), CancellationToken.None);

        var stored = await repository.GetByIdAsync(ruleId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("规则 A 已更新", stored!.Name);
        Assert.Equal("https://example.com/a2", stored.Url);
        Assert.False(stored.IsEnabled);
        Assert.Equal(TtsRuleCompatibilityStatus.CompatibleWithWarnings, stored.CompatibilityStatus);
        Assert.Equal(["customField"], stored.UnsupportedFields);
        Assert.Equal("""{"method":"POST"}""", stored.RequestOptionsJson);
    }

    [Fact]
    public async Task DeleteAsync_removes_rule()
    {
        var repository = await CreateRepositoryAsync();
        var utcNow = DateTime.UtcNow.ToString("O");
        var ruleId = await repository.SaveAsync(new HttpTtsRule(
            0,
            "待删除规则",
            "https://example.com/delete",
            null,
            null,
            null,
            null,
            false,
            null,
            """{"name":"待删除规则","url":"https://example.com/delete"}""",
            true,
            TtsRuleCompatibilityStatus.Compatible,
            [],
            null,
            utcNow,
            utcNow), CancellationToken.None);

        await repository.DeleteAsync(ruleId, CancellationToken.None);

        Assert.Null(await repository.GetByIdAsync(ruleId, CancellationToken.None));
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
