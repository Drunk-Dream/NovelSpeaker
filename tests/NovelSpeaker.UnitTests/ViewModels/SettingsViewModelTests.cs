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

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        private readonly AppSettings _loadedSettings;

        public FakeAppSettingsStore(AppSettings loadedSettings)
        {
            _loadedSettings = loadedSettings;
        }

        public AppSettings? LastSavedSettings { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_loadedSettings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            LastSavedSettings = settings;
            return Task.CompletedTask;
        }
    }
}
