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
    public async Task ImportJsonTextAsync_renames_same_name_rules_and_reports_counts()
    {
        var repository = new FakeTtsRuleRepository([
            new HttpTtsRule(
                3,
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
        var service = new TtsRuleLibraryService(repository, new FakeAppSettingsStore(AppSettings.Default), new LegadoRuleConverter());

        var result = await service.ImportJsonTextAsync(
            """
            [
              {"name":"现有规则","url":"https://example.com/new"},
              {"name":"无效规则"},
              {"name":"现有规则","url":"https://example.com/old"}
            ]
            """,
            "file.json",
            CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.NotNull(result.FirstImportedRuleId);
        Assert.Contains(repository.Rules, rule => rule.Name == "现有规则 (2)");
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

    [Fact]
    public async Task GetEditorAsync_and_SaveEditorAsync_roundtrip_structured_request_options()
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        var repository = new FakeTtsRuleRepository([
            new HttpTtsRule(
                12,
                "可编辑规则",
                "https://example.com/tts",
                "audio/mpeg",
                "2/1000",
                """{"Authorization":"Bearer demo"}""",
                """{"method":"POST","headers":{"X-Test":"1"},"body":{"text":"{{speakText}}"},"timeoutMs":5000}""",
                true,
                123,
                """{"name":"可编辑规则","url":"https://example.com/tts","contentType":"audio/mpeg","concurrentRate":"2/1000","header":"{\"Authorization\":\"Bearer demo\"}","requestOptions":{"method":"POST","headers":{"X-Test":"1"},"body":{"text":"{{speakText}}"},"timeoutMs":5000},"enabledCookieJar":true,"lastUpdateTime":123}""",
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                utcNow,
                utcNow,
                utcNow)
            {
                LoginInfoJson = """{"token":"abc"}"""
            }
        ]);
        var service = new TtsRuleLibraryService(repository, new FakeAppSettingsStore(AppSettings.Default), new LegadoRuleConverter());

        var editor = await service.GetEditorAsync(12, CancellationToken.None);
        var saved = await service.SaveEditorAsync(editor! with
        {
            Name = "已更新规则",
            LoginInfo = [new TtsRuleEditorKeyValue("token", "updated-secret")],
            RequestOptions = editor.RequestOptions with
            {
                TimeoutMs = 8000
            },
            RawRuleJson = string.Empty
        }, CancellationToken.None);

        Assert.NotNull(editor);
        Assert.Equal("POST", editor!.RequestOptions.Method);
        Assert.Single(editor.RequestOptions.Headers);
        Assert.Single(editor.LoginInfo);
        Assert.Equal("已更新规则", saved.Name);
        Assert.Contains(@"""timeoutMs"":8000", saved.RequestOptionsJson);
        Assert.Equal("""{"token":"updated-secret"}""", saved.LoginInfoJson);
    }

    [Fact]
    public async Task SaveEditorAsync_renames_duplicate_names()
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        var repository = new FakeTtsRuleRepository([
            new HttpTtsRule(
                1,
                "重复名",
                "https://example.com/one",
                null,
                null,
                null,
                null,
                false,
                null,
                """{"name":"重复名","url":"https://example.com/one"}""",
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                null,
                utcNow,
                utcNow)
        ]);
        var service = new TtsRuleLibraryService(repository, new FakeAppSettingsStore(AppSettings.Default), new LegadoRuleConverter());

        var saved = await service.SaveEditorAsync(
            new TtsRuleEditorModel(
                null,
                "重复名",
                true,
                "https://example.com/two",
                null,
                null,
                false,
                null,
                [],
                new TtsRuleRequestOptionsEditor(null, [], null, null),
                string.Empty,
                TtsRuleCompatibilityStatus.Compatible,
                []),
            CancellationToken.None);

        Assert.Equal("重复名 (2)", saved.Name);
    }

    [Fact]
    public async Task ValidateEditorAsync_rejects_raw_json_mismatch()
    {
        var service = new TtsRuleLibraryService(
            new FakeTtsRuleRepository([]),
            new FakeAppSettingsStore(AppSettings.Default),
            new LegadoRuleConverter());
        var editor = new TtsRuleEditorModel(
            null,
            "规则 A",
            true,
            "https://example.com/tts",
            null,
            null,
            false,
            null,
            [],
            new TtsRuleRequestOptionsEditor(null, [], null, null),
            """{"name":"别的规则","url":"https://example.com/tts"}""",
            TtsRuleCompatibilityStatus.Compatible,
            []);

        var validation = await service.ValidateEditorAsync(editor, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("不一致", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyRuleMutationAsync_switches_to_replacement_when_disabling_current_rule()
    {
        var repository = new FakeTtsRuleRepository([
            new HttpTtsRule(
                1,
                "当前规则",
                "https://example.com/a",
                null,
                null,
                null,
                null,
                false,
                null,
                """{"name":"当前规则","url":"https://example.com/a"}""",
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                null,
                "created",
                "updated"),
            new HttpTtsRule(
                2,
                "替代规则",
                "https://example.com/b",
                null,
                null,
                null,
                null,
                false,
                null,
                """{"name":"替代规则","url":"https://example.com/b"}""",
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                null,
                "created",
                "updated")
        ]);
        var settingsStore = new FakeAppSettingsStore(AppSettings.Default with { SelectedTtsRuleId = 1 });
        var service = new TtsRuleLibraryService(repository, settingsStore, new LegadoRuleConverter());

        var protection = await service.GetRuleProtectionAsync(1, TtsRuleMutationAction.Disable, CancellationToken.None);
        var result = await service.ApplyRuleMutationAsync(
            new TtsRuleMutationDecision(1, TtsRuleMutationAction.Disable, 2, false),
            CancellationToken.None);

        Assert.False(protection.CanApplyDirectly);
        Assert.Single(protection.ReplacementCandidates);
        Assert.Equal(2, result.SelectedRuleId);
        Assert.Equal(2, settingsStore.Current.SelectedTtsRuleId);
        Assert.False(repository.Rules.Single(rule => rule.Id == 1).IsEnabled);
    }

    [Fact]
    public async Task ApplyRuleMutationAsync_can_clear_current_rule_even_when_replacements_exist()
    {
        var repository = new FakeTtsRuleRepository([
            new HttpTtsRule(
                1,
                "当前规则",
                "https://example.com/a",
                null,
                null,
                null,
                null,
                false,
                null,
                """{"name":"当前规则","url":"https://example.com/a"}""",
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                null,
                "created",
                "updated"),
            new HttpTtsRule(
                2,
                "候选规则",
                "https://example.com/b",
                null,
                null,
                null,
                null,
                false,
                null,
                """{"name":"候选规则","url":"https://example.com/b"}""",
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                null,
                "created",
                "updated")
        ]);
        var settingsStore = new FakeAppSettingsStore(AppSettings.Default with { SelectedTtsRuleId = 1 });
        var service = new TtsRuleLibraryService(repository, settingsStore, new LegadoRuleConverter());

        var protection = await service.GetRuleProtectionAsync(1, TtsRuleMutationAction.Disable, CancellationToken.None);
        var result = await service.ApplyRuleMutationAsync(
            new TtsRuleMutationDecision(1, TtsRuleMutationAction.Disable, null, true),
            CancellationToken.None);

        Assert.True(protection.CanClearSelectedRule);
        Assert.Null(result.SelectedRuleId);
        Assert.Null(settingsStore.Current.SelectedTtsRuleId);
        Assert.False(repository.Rules.Single(rule => rule.Id == 1).IsEnabled);
    }

    private sealed class FakeAppSettingsStore : IAppSettingsService
    {
        public FakeAppSettingsStore(AppSettings current)
        {
            Current = current;
        }

        public AppSettings Current { get; set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            Current = (Current with
            {
                SelectedTtsRuleId = update.ClearSelectedTtsRuleId ? null : update.SelectedTtsRuleId ?? Current.SelectedTtsRuleId
            }).Normalize();
            return Task.FromResult(Current);
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
