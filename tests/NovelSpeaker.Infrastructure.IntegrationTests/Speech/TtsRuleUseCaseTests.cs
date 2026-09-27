using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Domain.Speech.Providers;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Speech;

public sealed class TtsRuleUseCaseTests
{
    [Fact]
    public async Task Editor_uses_copy_normalizes_fields_and_preserves_persisted_rule_until_save()
    {
        var repository = new FakeRepository([Rule(4, "原规则", "https://example.com/original")]);
        using var provider = CreateProvider(repository, AppSettings.Default);
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
    public async Task Editor_validation_accepts_http_headers_and_syntactic_templates(string key, string value)
    {
        using var provider = CreateProvider(new FakeRepository([]), AppSettings.Default);
        var editor = new TtsRuleEditorModel(null, "规则", true, "https://example.com", null, null, null,
            [new TtsRuleEditorKeyValue(key, value)], new TtsRuleRequestOptionsEditor("GET", null));

        var result = await provider.GetRequiredService<ITtsRuleEditorUseCase>().ValidateEditorAsync(editor, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Legacy_selection_and_mutations_are_unavailable_during_provider_migration()
    {
        var repository = new FakeRepository([Rule(1, "当前", "https://example.com/a"), Rule(2, "候选", "https://example.com/b")]);
        var currentProviderId = ProviderId.FromLegacyHttpTtsRuleId(1);
        using var provider = CreateProvider(repository, AppSettings.Default with { CurrentProviderId = currentProviderId });
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
        using var provider = CreateProvider(new FakeRepository([]), AppSettings.Default);
        var editor = new TtsRuleEditorModel(null, name, true, url, null, concurrentRate, null, [], new(method, body));

        var result = await provider.GetRequiredService<ITtsRuleEditorUseCase>().ValidateEditorAsync(editor, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Legacy_selection_does_not_write_provider_settings_or_rule_usage()
    {
        var repository = new FakeRepository([Rule(1, "当前", "https://example.com/a"), Rule(2, "禁用", "https://example.com/b") with { IsEnabled = false }]);
        using var provider = CreateProvider(repository, AppSettings.Default);
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
            AppSettings.Default);

        Assert.Empty(await provider.GetRequiredService<ITtsRuleQueries>().GetRulesAsync(CancellationToken.None));
        Assert.Null(await provider.GetRequiredService<ITtsRuleQueries>().ExportRuleJsonAsync(1, CancellationToken.None));
    }

    private static ServiceProvider CreateProvider(FakeRepository repository, AppSettings settings)
    {
        var services = new ServiceCollection();
        services.AddNovelSpeakerApplication(settings);
        services.AddSingleton<ITtsRuleRepository>(repository);
        services.AddSingleton<IAppSettingsStore>(new FakeSettingsStore(settings));
        return services.BuildServiceProvider();
    }

    private static HttpTtsRule Rule(long id, string name, string url) =>
        new(id, name, url, null, null, new Dictionary<string, string>(), null, null, false, null, true, null,
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:00:00Z"));

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
