using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Speech.Legado;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class TtsRuleUseCaseTests
{
    [Fact]
    public async Task Import_skips_exact_duplicate_renames_same_name_and_selects_first_enabled_rule()
    {
        var existing = Rule(1, "同名", "https://example.com/old");
        var repository = new FakeRepository([existing]);
        var source = new FakeSourceAdapter(new TtsRuleSourceReadResult([
            Item(0, Rule(0, "同名", "https://example.com/old")),
            Item(1, Rule(0, "同名", "https://example.com/new")),
            new TtsRuleSourceItem(2, new TtsRuleConversionResult(Rule(0, "无效", string.Empty), [], ["缺少 url。"]), null)
        ], null));
        using var provider = CreateProvider(repository, source, AppSettings.Default);

        var result = await provider.GetRequiredService<ITtsRuleImportUseCase>()
            .ImportJsonTextAsync("source", "剪贴板", CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Contains(repository.Rules, rule => rule.Name == "同名 (2)");
        Assert.Equal(result.FirstImportedRuleId, provider.GetRequiredService<IAppSettingsService>().Current.SelectedTtsRuleId);
    }

    [Fact]
    public async Task Editor_uses_copy_normalizes_fields_and_preserves_persisted_rule_until_save()
    {
        var repository = new FakeRepository([Rule(4, "原规则", "https://example.com/original")]);
        using var provider = CreateProvider(repository, new FakeSourceAdapter(new([], null)), AppSettings.Default);
        var editorUseCase = provider.GetRequiredService<ITtsRuleEditorUseCase>();

        var editor = await editorUseCase.GetEditorAsync(4, CancellationToken.None);
        var changed = editor! with { Name = "  修改后  ", Url = " https://example.com/changed " };

        Assert.Equal("原规则", repository.Rules.Single().Name);
        var saved = await editorUseCase.SaveEditorAsync(changed, CancellationToken.None);
        Assert.Equal("修改后", saved.Name);
        Assert.Equal("https://example.com/changed", saved.Url);
    }

    [Theory]
    [InlineData("Cookie", "secret")]
    [InlineData("X-Token", "{{loginInfo.token}}")]
    public async Task Editor_validation_rejects_cookie_and_login_info_without_echoing_value(string key, string value)
    {
        using var provider = CreateProvider(new FakeRepository([]), new FakeSourceAdapter(new([], null)), AppSettings.Default);
        var editor = new TtsRuleEditorModel(null, "规则", true, "https://example.com", null, null, null,
            [new TtsRuleEditorKeyValue(key, value)], new TtsRuleRequestOptionsEditor("GET", null));

        var result = await provider.GetRequiredService<ITtsRuleEditorUseCase>().ValidateEditorAsync(editor, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Cookie/LoginInfo", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Selection_protects_and_clears_current_rule_when_disabling_or_deleting()
    {
        var repository = new FakeRepository([Rule(1, "当前", "https://example.com/a"), Rule(2, "候选", "https://example.com/b")]);
        using var provider = CreateProvider(repository, new FakeSourceAdapter(new([], null)), AppSettings.Default with { SelectedTtsRuleId = 1 });
        var selection = provider.GetRequiredService<ITtsRuleSelectionUseCase>();

        var protection = await selection.GetRuleProtectionAsync(1, TtsRuleMutationAction.Disable, CancellationToken.None);
        var disabled = await selection.ApplyRuleMutationAsync(new(1, TtsRuleMutationAction.Disable, null, true), CancellationToken.None);

        Assert.False(protection.CanApplyDirectly);
        Assert.Null(disabled.SelectedRuleId);
        Assert.False(repository.Rules.Single(rule => rule.Id == 1).IsEnabled);

        await selection.SelectRuleAsync(2, CancellationToken.None);
        await selection.ApplyRuleMutationAsync(new(2, TtsRuleMutationAction.Delete, null, true), CancellationToken.None);
        Assert.DoesNotContain(repository.Rules, rule => rule.Id == 2);
        Assert.Null(provider.GetRequiredService<IAppSettingsService>().Current.SelectedTtsRuleId);
    }

    [Fact]
    public async Task Queries_and_editor_export_emit_canonical_structured_json()
    {
        var rule = Rule(7, "结构化", "https://example.com") with
        {
            Headers = new Dictionary<string, string> { ["X-Test"] = "1" },
            RequestMethod = "POST",
            RequestBody = "{\"text\":\"{{speakText}}\"}",
            RequestBodyIsJsonStructure = true
        };
        using var provider = CreateProvider(new FakeRepository([rule]), new FakeSourceAdapter(new([], null)), AppSettings.Default);

        var json = await provider.GetRequiredService<ITtsRuleQueries>().ExportRuleJsonAsync(7, CancellationToken.None);

        Assert.Equal("""{"name":"结构化","url":"https://example.com","header":"{\"X-Test\":\"1\"}","requestOptions":{"method":"POST","body":{"text":"{{speakText}}"}}}""", json);
    }

    [Fact]
    public async Task Editor_renames_duplicate_name_and_exports_normalized_draft_without_saving()
    {
        var repository = new FakeRepository([Rule(1, "重复", "https://example.com/one")]);
        using var provider = CreateProvider(repository, new FakeSourceAdapter(new([], null)), AppSettings.Default);
        var editorUseCase = provider.GetRequiredService<ITtsRuleEditorUseCase>();
        var draft = new TtsRuleEditorModel(null, "  重复  ", true, " https://example.com/two ", null, " 2/1000 ", null, [], new(null, null));

        var exported = await editorUseCase.ExportEditorJsonAsync(draft, CancellationToken.None);
        Assert.Single(repository.Rules);
        Assert.Equal("""{"name":"重复","url":"https://example.com/two","concurrentRate":"2/1000"}""", exported);

        var saved = await editorUseCase.SaveEditorAsync(draft, CancellationToken.None);
        Assert.Equal("重复 (2)", saved.Name);
    }

    [Fact]
    public async Task Editor_prepares_structured_draft_rule_without_saving()
    {
        var repository = new FakeRepository([]);
        using var provider = CreateProvider(repository, new FakeSourceAdapter(new([], null)), AppSettings.Default);
        var editor = new TtsRuleEditorModel(
            null,
            "  试听草稿  ",
            true,
            " https://example.com/tts ",
            "audio/mpeg",
            null,
            null,
            [new TtsRuleEditorKeyValue("X-Test", "1")],
            new TtsRuleRequestOptionsEditor("post", "{\"text\":\"{{speakText}}\"}"));

        var result = await provider.GetRequiredService<ITtsRuleEditorUseCase>()
            .PrepareDraftAsync(editor, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Empty(repository.Rules);
        Assert.Equal(0, repository.SaveCallCount);
        Assert.Equal("试听草稿", result.CandidateRule!.Name);
        Assert.Equal("POST", result.CandidateRule.RequestMethod);
        Assert.Equal("{\"text\":\"{{speakText}}\"}", result.CandidateRule.RequestBody);
        Assert.True(result.CandidateRule.RequestBodyIsJsonStructure);
        Assert.Equal("1", result.CandidateRule.Headers["X-Test"]);
    }

    [Theory]
    [InlineData("规则", "https://example.com", "PUT", null, null, "requestOptions.method")]
    [InlineData("规则", "https://example.com", "GET", "body", null, "GET 请求")]
    [InlineData("规则", "https://example.com/{{", "GET", null, null, "URL 模板")]
    [InlineData("规则", "https://example.com", "GET", null, "invalid", "并发限制")]
    [InlineData("", "https://example.com", "GET", null, null, "规则名称")]
    public async Task Editor_validation_preserves_field_errors(
        string name,
        string url,
        string method,
        string? body,
        string? concurrentRate,
        string expected)
    {
        using var provider = CreateProvider(new FakeRepository([]), new FakeSourceAdapter(new([], null)), AppSettings.Default);
        var editor = new TtsRuleEditorModel(null, name, true, url, null, concurrentRate, null, [], new(method, body));

        var result = await provider.GetRequiredService<ITtsRuleEditorUseCase>().ValidateEditorAsync(editor, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Selection_sets_current_updates_last_used_and_rejects_invalid_replacement()
    {
        var repository = new FakeRepository([Rule(1, "当前", "https://example.com/a"), Rule(2, "禁用", "https://example.com/b") with { IsEnabled = false }]);
        using var provider = CreateProvider(repository, new FakeSourceAdapter(new([], null)), AppSettings.Default);
        var selection = provider.GetRequiredService<ITtsRuleSelectionUseCase>();

        await selection.SelectRuleAsync(1, CancellationToken.None);
        Assert.Equal(1, provider.GetRequiredService<IAppSettingsService>().Current.SelectedTtsRuleId);
        Assert.NotNull(repository.Rules.Single(rule => rule.Id == 1).LastUsedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => selection.ApplyRuleMutationAsync(
            new(1, TtsRuleMutationAction.Disable, 2, false), CancellationToken.None));
    }

    [Fact]
    public async Task Selection_can_replace_current_rule_when_disabling_it()
    {
        var repository = new FakeRepository([Rule(1, "当前", "https://example.com/a"), Rule(2, "替代", "https://example.com/b")]);
        using var provider = CreateProvider(repository, new FakeSourceAdapter(new([], null)), AppSettings.Default with { SelectedTtsRuleId = 1 });

        var result = await provider.GetRequiredService<ITtsRuleSelectionUseCase>().ApplyRuleMutationAsync(
            new(1, TtsRuleMutationAction.Disable, 2, false), CancellationToken.None);

        Assert.Equal(2, result.SelectedRuleId);
        Assert.Equal(2, provider.GetRequiredService<IAppSettingsService>().Current.SelectedTtsRuleId);
    }

    [Fact]
    public async Task Queries_marks_only_enabled_selected_rule()
    {
        var repository = new FakeRepository([Rule(1, "当前", "https://example.com/a"), Rule(2, "其它", "https://example.com/b")]);
        using var provider = CreateProvider(repository, new FakeSourceAdapter(new([], null)), AppSettings.Default with { SelectedTtsRuleId = 1 });

        var summaries = await provider.GetRequiredService<ITtsRuleQueries>().GetRulesAsync(CancellationToken.None);

        Assert.True(summaries.Single(rule => rule.Id == 1).IsSelected);
        Assert.False(summaries.Single(rule => rule.Id == 2).IsSelected);
    }

    [Fact]
    public async Task Import_propagates_cancellation_before_source_read()
    {
        var source = new CountingSourceAdapter(new([], null));
        using var provider = CreateProvider(new FakeRepository([]), source, AppSettings.Default);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRequiredService<ITtsRuleImportUseCase>()
            .CreateImportPreviewAsync("{}", "file", cancellation.Token));
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task Canonical_json_import_export_roundtrip_preserves_bytes_and_structure()
    {
        const string canonical =
            """{"name":"结构化","url":"https://example.com","header":"{\"Authorization\":\"Bearer demo\"}","requestOptions":{"method":"POST","body":{"text":"{{speakText}}"}},"lastUpdateTime":123}""";
        var repository = new FakeRepository([]);
        var adapter = new LegadoRuleSourceAdapter(new LegadoRuleSourceParser(), new LegadoRuleConverter());
        using var provider = CreateProvider(repository, adapter, AppSettings.Default);

        var imported = await provider.GetRequiredService<ITtsRuleImportUseCase>()
            .ImportJsonTextAsync(canonical, "export.json", CancellationToken.None);
        var exported = await provider.GetRequiredService<ITtsRuleQueries>()
            .ExportRuleJsonAsync(imported.FirstImportedRuleId!.Value, CancellationToken.None);

        Assert.Equal(canonical, exported);
        Assert.True(repository.Rules.Single().RequestBodyIsJsonStructure);
    }

    [Fact]
    public async Task Import_rejects_cookie_and_login_info_items_while_committing_valid_item()
    {
        var repository = new FakeRepository([]);
        var adapter = new LegadoRuleSourceAdapter(new LegadoRuleSourceParser(), new LegadoRuleConverter());
        using var provider = CreateProvider(repository, adapter, AppSettings.Default);
        const string json = """
            [
              {"name":"有效","url":"https://example.com/valid"},
              {"name":"Cookie","url":"https://example.com/cookie","enabledCookieJar":true},
              {"name":"Login","url":"https://example.com/login?token={{loginInfo.token}}"}
            ]
            """;

        var result = await provider.GetRequiredService<ITtsRuleImportUseCase>()
            .ImportJsonTextAsync(json, "file.json", CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal("有效", Assert.Single(repository.Rules).Name);
    }

    private static ServiceProvider CreateProvider(FakeRepository repository, ITtsRuleSourceAdapter source, AppSettings settings)
    {
        var services = new ServiceCollection();
        services.AddNovelSpeakerApplication(settings);
        services.AddSingleton<ITtsRuleRepository>(repository);
        services.AddSingleton(source);
        services.AddSingleton<IAppSettingsStore>(new FakeSettingsStore(settings));
        return services.BuildServiceProvider();
    }

    private static TtsRuleSourceItem Item(int index, HttpTtsRule rule) =>
        new(index, new TtsRuleConversionResult(rule, [], []), null);

    private static HttpTtsRule Rule(long id, string name, string url) =>
        new(id, name, url, null, null, new Dictionary<string, string>(), null, null, false, null, true, null,
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:00:00Z"));

    private sealed class FakeSourceAdapter(TtsRuleSourceReadResult result) : ITtsRuleSourceAdapter
    {
        public TtsRuleSourceReadResult Read(string jsonText) => result;
    }

    private sealed class CountingSourceAdapter(TtsRuleSourceReadResult result) : ITtsRuleSourceAdapter
    {
        public int ReadCount { get; private set; }

        public TtsRuleSourceReadResult Read(string jsonText)
        {
            ReadCount++;
            return result;
        }
    }

    private sealed class FakeSettingsStore(AppSettings settings) : IAppSettingsStore
    {
        private AppSettings _settings = settings;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings updatedSettings, CancellationToken cancellationToken)
        {
            _settings = updatedSettings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository(IReadOnlyList<HttpTtsRule> rules) : ITtsRuleRepository
    {
        public List<HttpTtsRule> Rules { get; } = rules.ToList();
        public int SaveCallCount { get; private set; }
        public Task<IReadOnlyList<HttpTtsRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HttpTtsRule>>(Rules.ToArray());
        public Task<HttpTtsRule?> GetByIdAsync(long ruleId, CancellationToken cancellationToken) => Task.FromResult<HttpTtsRule?>(Rules.FirstOrDefault(rule => rule.Id == ruleId));
        public Task<long> SaveAsync(HttpTtsRule rule, CancellationToken cancellationToken)
        {
            SaveCallCount++;
            var id = rule.Id > 0 ? rule.Id : Rules.Count == 0 ? 1 : Rules.Max(item => item.Id) + 1;
            var index = Rules.FindIndex(item => item.Id == id);
            if (index >= 0) Rules[index] = rule with { Id = id };
            else Rules.Add(rule with { Id = id });
            return Task.FromResult(id);
        }
        public Task DeleteAsync(long ruleId, CancellationToken cancellationToken)
        {
            Rules.RemoveAll(rule => rule.Id == ruleId);
            return Task.CompletedTask;
        }
    }
}
