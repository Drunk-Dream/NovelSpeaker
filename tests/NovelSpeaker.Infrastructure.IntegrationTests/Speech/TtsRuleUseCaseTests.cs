using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.Speech.Legado;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Speech;

public sealed class TtsRuleUseCaseTests
{
    [Fact]
    public async Task Import_skips_exact_duplicate_renames_same_name_and_does_not_select_imported_rule()
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

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.DoesNotContain(repository.Rules, rule => rule.Name == "同名 (2)");
        Assert.Null(provider.GetRequiredService<IAppSettingsService>().Current.CurrentProviderId);
    }

    [Fact]
    public async Task Import_reclassifies_preview_duplicates_against_latest_repository_snapshot()
    {
        var duplicate = Rule(1, "原重复", "https://example.com/duplicate");
        var repository = new FakeRepository([duplicate]);
        var source = new FakeSourceAdapter(new TtsRuleSourceReadResult(
        [
            Item(0, duplicate with { Id = 0 }),
            Item(1, Rule(0, "另一条", "https://example.com/other"))
        ], null));
        using var provider = CreateProvider(repository, source, AppSettings.Default);
        var import = provider.GetRequiredService<ITtsRuleImportUseCase>();
        var preview = await import.CreateImportPreviewAsync("source", "file", CancellationToken.None);
        Assert.True(preview.Items[0].IsDuplicate);
        repository.Rules.Clear();

        var result = await import.ImportAsync(preview, CancellationToken.None);

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(["原重复", "另一条"], repository.Rules.Select(rule => rule.Name));
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

    [Fact]
    public async Task Legacy_rule_edit_does_not_invalidate_provider_coverage()
    {
        var repository = new FakeRepository([Rule(4, "原规则", "https://example.com/original")]);
        using var provider = CreateProvider(
            repository,
            new FakeSourceAdapter(new([], null)),
            AppSettings.Default with { CurrentProviderId = ProviderId.FromLegacyHttpTtsRuleId(4) });
        var invalidationCoordinator = provider.GetRequiredService<ICacheInvalidationCoordinator>();
        var batches = new List<CacheInvalidationBatch>();
        invalidationCoordinator.BatchPublished += (_, batch) => batches.Add(batch);
        var editorUseCase = provider.GetRequiredService<ITtsRuleEditorUseCase>();
        var editor = await editorUseCase.GetEditorAsync(4, CancellationToken.None);

        await editorUseCase.SaveEditorAsync(
            editor! with { Url = "https://example.com/changed" },
            CancellationToken.None);
        await invalidationCoordinator.FlushPendingAsync(CancellationToken.None);

        Assert.Empty(batches);
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
    public async Task Legacy_selection_and_mutations_are_unavailable_during_provider_migration()
    {
        var repository = new FakeRepository([Rule(1, "当前", "https://example.com/a"), Rule(2, "候选", "https://example.com/b")]);
        var currentProviderId = ProviderId.FromLegacyHttpTtsRuleId(1);
        using var provider = CreateProvider(repository, new FakeSourceAdapter(new([], null)), AppSettings.Default with { CurrentProviderId = currentProviderId });
        var selection = provider.GetRequiredService<ITtsRuleSelectionUseCase>();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            selection.GetRuleProtectionAsync(1, TtsRuleMutationAction.Disable, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => selection.ApplyRuleMutationAsync(
            new(1, TtsRuleMutationAction.Disable, null, true), CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => selection.SelectRuleAsync(2, CancellationToken.None));
        Assert.All(repository.Rules, rule => Assert.True(rule.IsEnabled));
        Assert.Equal(currentProviderId, provider.GetRequiredService<IAppSettingsService>().Current.CurrentProviderId);
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
    public async Task Legacy_selection_does_not_write_provider_settings_or_rule_usage()
    {
        var repository = new FakeRepository([Rule(1, "当前", "https://example.com/a"), Rule(2, "禁用", "https://example.com/b") with { IsEnabled = false }]);
        using var provider = CreateProvider(repository, new FakeSourceAdapter(new([], null)), AppSettings.Default);
        var selection = provider.GetRequiredService<ITtsRuleSelectionUseCase>();

        await Assert.ThrowsAsync<NotSupportedException>(() => selection.SelectRuleAsync(1, CancellationToken.None));
        Assert.Null(provider.GetRequiredService<IAppSettingsService>().Current.CurrentProviderId);
        Assert.Null(repository.Rules.Single(rule => rule.Id == 1).LastUsedAt);
    }

    [Fact]
    public async Task Legacy_rule_queries_return_unavailable_data_during_provider_migration()
    {
        using var provider = CreateProvider(
            new FakeRepository([Rule(1, "旧规则", "https://example.com")]),
            new FakeSourceAdapter(new([], null)),
            AppSettings.Default);

        Assert.Empty(await provider.GetRequiredService<ITtsRuleQueries>().GetRulesAsync(CancellationToken.None));
        Assert.Null(await provider.GetRequiredService<ITtsRuleQueries>().ExportRuleJsonAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Import_rejects_entire_batch_when_cookie_or_login_info_item_is_invalid()
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

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(3, result.FailedCount);
        Assert.Empty(repository.Rules);
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
