using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task LoadAsync_populates_segmentation_settings_from_store()
    {
        var store = new FakeAppSettingsStore(new AppSettings(false, 120, 14, 3, "Warning", "Dark", "{{name}} / {{author}}"));
        var viewModel = new SettingsViewModel(store, new FakeNavigationService());

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.EnableLongParagraphSplitting);
        Assert.Equal(120, viewModel.LongParagraphThreshold);
        Assert.Equal(14, viewModel.DefaultSpeakSpeed);
        Assert.Equal(2, viewModel.PrefetchCount);
        Assert.Equal("Warning", viewModel.SelectedLogLevel);
        Assert.Equal("Dark", viewModel.SelectedTheme);
        Assert.Equal("{{name}} / {{author}}", viewModel.BookFileNameTemplate);
    }

    [Fact]
    public async Task SaveAsync_persists_updated_segmentation_settings()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var viewModel = new SettingsViewModel(store, new FakeNavigationService())
        {
            EnableLongParagraphSplitting = false,
            LongParagraphThreshold = 25,
            DefaultSpeakSpeed = 16,
            PrefetchCount = -1,
            SelectedLogLevel = "Error",
            SelectedTheme = "Light",
            BookFileNameTemplate = "  {{name}} - {{author}}  "
        };

        await viewModel.SaveAsync(CancellationToken.None);

        Assert.NotNull(store.LastSavedSettings);
        Assert.False(store.LastSavedSettings!.EnableLongParagraphSplitting);
        Assert.Equal(25, store.LastSavedSettings.LongParagraphThreshold);
        Assert.Equal(16, store.LastSavedSettings.DefaultSpeakSpeed);
        Assert.Equal(-1, store.LastSavedSettings.PrefetchCount);
        Assert.Equal("Error", store.LastSavedSettings.LogLevel);
        Assert.Equal("Light", store.LastSavedSettings.Theme);
        Assert.Equal("  {{name}} - {{author}}  ", store.LastSavedSettings.BookFileNameTemplate);
        Assert.Equal(2, viewModel.PrefetchCount);
        Assert.Equal("{{name}} - {{author}}", viewModel.BookFileNameTemplate);
    }

    [Fact]
    public void OpenChapterRulesCommand_navigates_to_chapter_rules_section()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = new SettingsViewModel(new FakeAppSettingsStore(AppSettings.Default), navigationService);

        viewModel.OpenChapterRulesCommand.Execute(null);

        Assert.Equal(SettingsSection.ChapterRules, navigationService.LastSettingsSection);
    }

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        public FakeAppSettingsStore(AppSettings loadedSettings)
        {
            CurrentSettings = loadedSettings.Normalize();
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
            CurrentSettings = settings.Normalize();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNavigationService : IAppNavigationService
    {
        public AppNavigationEntry CurrentEntry { get; private set; } = AppNavigationEntry.CreatePrimary(AppPrimaryDestination.Library);

        public bool CanGoBack => false;

        public SettingsSection? LastSettingsSection { get; private set; }

        public event EventHandler<AppNavigationChangedEventArgs>? CurrentEntryChanged;

        public bool NavigateToPrimary(AppPrimaryDestination destination) => true;

        public bool NavigateToSettings(SettingsSection section)
        {
            LastSettingsSection = section;
            CurrentEntry = AppNavigationEntry.CreateSettings(section);
            CurrentEntryChanged?.Invoke(this, new AppNavigationChangedEventArgs(CurrentEntry));
            return true;
        }

        public bool NavigateToPlayer(PlayerNavigationRequest request) => true;

        public bool NavigateToBookDetails(BookDetailsNavigationRequest request) => true;

        public bool GoBack() => false;
    }
}
