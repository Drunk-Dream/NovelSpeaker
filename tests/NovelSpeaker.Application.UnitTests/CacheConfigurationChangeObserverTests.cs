using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class CacheConfigurationChangeObserverTests
{
    [Fact]
    public void Observer_maps_only_cache_relevant_settings_and_selected_rule_changes()
    {
        var settings = new FakeSettingsService(AppSettings.Default with { SelectedTtsRuleId = 4 });
        var ttsRules = new FakeTtsRuleEditor();
        var regexRules = new FakeRegexRuleWorkspace();
        var invalidations = new List<CacheInvalidation>();
        using var observer = new CacheConfigurationChangeObserver(
            settings,
            ttsRules,
            regexRules,
            invalidations.Add);

        settings.Raise(settings.Current with { PrefetchCount = settings.Current.PrefetchCount + 1 });
        Assert.Empty(invalidations);

        settings.Raise(settings.Current with { DefaultSpeakSpeed = settings.Current.DefaultSpeakSpeed + 1 });
        ttsRules.Raise(3);
        ttsRules.Raise(4);
        regexRules.Raise(RegexReplacementRulesChangeKind.Saved, affectsSpeechProfile: false);
        Assert.Equal(2, invalidations.Count);
        regexRules.Raise(RegexReplacementRulesChangeKind.Saved, affectsSpeechProfile: true);

        Assert.Equal(3, invalidations.Count);
        Assert.All(invalidations, invalidation =>
        {
            Assert.IsType<CacheInvalidationScope.Global>(invalidation.Scope);
            Assert.Equal(CacheInvalidationAspect.Coverage, invalidation.Aspects);
        });
    }

    [Fact]
    public void Observer_maps_regex_changes_and_unsubscribes_on_dispose()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        var ttsRules = new FakeTtsRuleEditor();
        var regexRules = new FakeRegexRuleWorkspace();
        var invalidations = new List<CacheInvalidation>();
        var observer = new CacheConfigurationChangeObserver(
            settings,
            ttsRules,
            regexRules,
            invalidations.Add);

        regexRules.Raise(RegexReplacementRulesChangeKind.Reordered);
        Assert.Single(invalidations);

        observer.Dispose();
        regexRules.Raise(RegexReplacementRulesChangeKind.Saved);
        settings.Raise(settings.Current with { ReadChapterTitle = true });
        ttsRules.Raise(1);

        Assert.Single(invalidations);
    }

    private sealed class FakeSettingsService(AppSettings current) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = current;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed;

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Raise(AppSettings next)
        {
            var previous = Current;
            Current = next;
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, next));
        }
    }

    private sealed class FakeTtsRuleEditor : ITtsRuleEditorUseCase
    {
        public event EventHandler<TtsRuleChangedEventArgs>? Changed;

        public Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TtsRuleValidationResult> ValidateEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TtsRuleDraftPreparationResult> PrepareDraftAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> ExportEditorJsonAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Raise(long ruleId) => Changed?.Invoke(this, new TtsRuleChangedEventArgs(ruleId));
    }

    private sealed class FakeRegexRuleWorkspace : IRegexReplacementRuleWorkspaceService
    {
        public event EventHandler<RegexReplacementRulesChangedEventArgs>? Changed;

        public Task<IReadOnlyList<RegexReplacementRuleListItem>> GetRulesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RegexReplacementRuleEditorModel?> GetEditorAsync(Guid ruleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RegexReplacementRuleEditorModel> SaveEditorAsync(RegexReplacementRuleEditorModel editor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetRuleEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> ExportRuleJsonAsync(Guid ruleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RuleJsonImportResult> ImportJsonAsync(string json, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveOrderAsync(IReadOnlyList<Guid> orderedRuleIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Raise(
            RegexReplacementRulesChangeKind kind,
            bool affectsSpeechProfile = true) =>
            Changed?.Invoke(
                this,
                new RegexReplacementRulesChangedEventArgs(kind, affectsSpeechProfile));
    }
}
