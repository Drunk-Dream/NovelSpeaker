using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task LoadAsync_populates_segmentation_settings_from_store()
    {
        var store = new FakeAppSettingsStore(new AppSettings(false, 120));
        var viewModel = new SettingsViewModel(store);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.EnableLongParagraphSplitting);
        Assert.Equal(120, viewModel.LongParagraphThreshold);
    }

    [Fact]
    public async Task SaveAsync_persists_updated_segmentation_settings()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var viewModel = new SettingsViewModel(store)
        {
            EnableLongParagraphSplitting = false,
            LongParagraphThreshold = 25
        };

        await viewModel.SaveAsync(CancellationToken.None);

        Assert.NotNull(store.LastSavedSettings);
        Assert.False(store.LastSavedSettings!.EnableLongParagraphSplitting);
        Assert.Equal(25, store.LastSavedSettings.LongParagraphThreshold);
    }

    [Fact]
    public async Task ToggleChapterRulesAsync_loads_chapter_rules_when_panel_is_opened()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var chapterRepository = new FakeChapterRuleRepository();
        var chapterRulesViewModel = new ChapterRulesViewModel(chapterRepository);
        var viewModel = new SettingsViewModel(store, chapterRulesViewModel);

        await viewModel.ToggleChapterRulesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsChapterRulesVisible);
        Assert.Equal(1, chapterRepository.GetAllCallCount);
    }

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        public FakeAppSettingsStore(AppSettings loadedSettings)
        {
            CurrentSettings = loadedSettings;
        }

        public AppSettings CurrentSettings { get; private set; }

        public AppSettings? LastSavedSettings { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(CurrentSettings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            LastSavedSettings = settings;
            CurrentSettings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChapterRuleRepository : Application.Books.IChapterRuleRepository
    {
        public int GetAllCallCount { get; private set; }

        public Task<IReadOnlyList<Domain.Books.ChapterRule>> GetAllAsync(CancellationToken cancellationToken)
        {
            GetAllCallCount++;
            return Task.FromResult<IReadOnlyList<Domain.Books.ChapterRule>>([]);
        }

        public Task<IReadOnlyList<Domain.Books.ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Domain.Books.ChapterRule>>([]);
        }

        public Task SaveAsync(Domain.Books.ChapterRule rule, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(string ruleId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> ImportDefaultsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
