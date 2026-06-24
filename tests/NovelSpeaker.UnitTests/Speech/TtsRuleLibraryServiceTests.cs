using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Speech.Rules;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class TtsRuleLibraryServiceTests
{
    [Fact]
    public async Task CreateImportPreviewAsync_marks_duplicates_name_conflicts_and_unsupported_fields()
    {
        var repository = new FakeTtsRuleRepository([
            new HttpTtsRule(
                7,
                "现有规则",
                "https://example.com/old",
                null,
                null,
                null,
                null,
                false,
                null,
                """{"name":"现有规则","url":"https://example.com/old"}""",
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                null,
                "created",
                "updated")
        ]);
        var settingsStore = new FakeAppSettingsStore(AppSettings.Default);
        var service = new TtsRuleLibraryService(repository, settingsStore, new LegadoRuleConverter());

        var preview = await service.CreateImportPreviewAsync(
            """
            [
              {"name":"现有规则","url":"https://example.com/old"},
              {"name":"现有规则","url":"https://example.com/new"},
              {"name":"带脚本规则","url":"https://example.com/js","jsLib":"demo"}
            ]
            """,
            "剪贴板",
            CancellationToken.None);

        Assert.Equal(3, preview.Items.Count);
        Assert.False(preview.Items[0].CanImport);
        Assert.True(preview.Items[0].IsDuplicate);
        Assert.True(preview.Items[1].CanImport);
        Assert.True(preview.Items[1].HasSameNameConflict);
        Assert.Equal(TtsRuleCompatibilityStatus.CompatibleWithWarnings, preview.Items[2].CompatibilityStatus);
        Assert.Equal(["jsLib"], preview.Items[2].UnsupportedFields);
    }

    [Fact]
    public async Task ImportAsync_persists_importable_items_and_exports_canonical_rule_json()
    {
        var repository = new FakeTtsRuleRepository([]);
        var settingsStore = new FakeAppSettingsStore(AppSettings.Default);
        var service = new TtsRuleLibraryService(repository, settingsStore, new LegadoRuleConverter());
        const string jsonText = """{"name":"新规则","url":"https://example.com/tts","header":{"X-Test":"1"}}""";

        var preview = await service.CreateImportPreviewAsync(jsonText, "file.json", CancellationToken.None);
        var result = await service.ImportAsync(preview, CancellationToken.None);
        var savedRule = Assert.Single(repository.Rules);
        var exportedJson = await service.ExportRuleJsonAsync(savedRule.Id, CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.NotEqual(jsonText, exportedJson);
        Assert.Equal("""{"name":"新规则","url":"https://example.com/tts","header":"{\"X-Test\":\"1\"}"}""", exportedJson);
    }

    [Fact]
    public async Task SelectRuleAsync_updates_last_used_and_settings()
    {
        var repository = new FakeTtsRuleRepository([
            new HttpTtsRule(
                5,
                "当前规则",
                "https://example.com/tts",
                null,
                null,
                null,
                null,
                false,
                null,
                """{"name":"当前规则","url":"https://example.com/tts"}""",
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                null,
                "created",
                "updated")
        ]);
        var settingsStore = new FakeAppSettingsStore(AppSettings.Default);
        var service = new TtsRuleLibraryService(repository, settingsStore, new LegadoRuleConverter());

        await service.SelectRuleAsync(5, CancellationToken.None);
        var summaries = await service.GetRulesAsync(CancellationToken.None);

        Assert.Equal(5, settingsStore.Current.SelectedTtsRuleId);
        Assert.True(Assert.Single(summaries).IsSelected);
        Assert.NotNull(repository.Rules[0].LastUsedAt);
    }

    [Fact]
    public async Task SetRuleEnabledAsync_and_DeleteRuleAsync_clear_selected_rule()
    {
        var repository = new FakeTtsRuleRepository([
            new HttpTtsRule(
                9,
                "当前规则",
                "https://example.com/tts",
                null,
                null,
                null,
                null,
                false,
                null,
                """{"name":"当前规则","url":"https://example.com/tts"}""",
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                null,
                "created",
                "updated")
        ]);
        var settingsStore = new FakeAppSettingsStore(AppSettings.Default with { SelectedTtsRuleId = 9 });
        var service = new TtsRuleLibraryService(repository, settingsStore, new LegadoRuleConverter());

        await service.SetRuleEnabledAsync(9, false, CancellationToken.None);
        Assert.Null(settingsStore.Current.SelectedTtsRuleId);

        await service.SelectRuleAsync(null, CancellationToken.None);
        settingsStore.Current = settingsStore.Current with { SelectedTtsRuleId = 9 };
        await service.DeleteRuleAsync(9, CancellationToken.None);
        Assert.Null(settingsStore.Current.SelectedTtsRuleId);
        Assert.Empty(repository.Rules);
    }

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        public FakeAppSettingsStore(AppSettings current)
        {
            Current = current;
        }

        public AppSettings Current { get; set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTtsRuleRepository : ITtsRuleRepository
    {
        public FakeTtsRuleRepository(IReadOnlyList<HttpTtsRule> rules)
        {
            Rules = rules.ToList();
        }

        public List<HttpTtsRule> Rules { get; }

        public Task<IReadOnlyList<HttpTtsRule>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<HttpTtsRule>>(Rules.ToArray());
        }

        public Task<HttpTtsRule?> GetByIdAsync(long ruleId, CancellationToken cancellationToken)
        {
            return Task.FromResult<HttpTtsRule?>(Rules.FirstOrDefault(rule => rule.Id == ruleId));
        }

        public Task<long> SaveAsync(HttpTtsRule rule, CancellationToken cancellationToken)
        {
            if (rule.Id <= 0)
            {
                var nextId = Rules.Count == 0 ? 1 : Rules.Max(item => item.Id) + 1;
                Rules.Add(rule with { Id = nextId });
                return Task.FromResult(nextId);
            }

            var index = Rules.FindIndex(item => item.Id == rule.Id);
            if (index >= 0)
            {
                Rules[index] = rule;
            }
            else
            {
                Rules.Add(rule);
            }

            return Task.FromResult(rule.Id);
        }

        public Task DeleteAsync(long ruleId, CancellationToken cancellationToken)
        {
            Rules.RemoveAll(rule => rule.Id == ruleId);
            return Task.CompletedTask;
        }
    }
}
