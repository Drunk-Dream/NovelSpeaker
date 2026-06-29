using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;
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
    public void OpenChapterRulesCommand_navigates_to_chapter_rules_page()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = new SettingsViewModel(new FakeAppSettingsStore(AppSettings.Default), navigationService);

        viewModel.OpenChapterRulesCommand.Execute(null);

        Assert.Equal(typeof(ChapterRulesPage), navigationService.LastNavigationPageType);
        Assert.True(navigationService.LastUsedHierarchyNavigation);
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

    private sealed class FakeNavigationService : INavigationService
    {
        public Type? LastNavigationPageType { get; private set; }

        public object? LastNavigationData { get; private set; }

        public bool LastUsedHierarchyNavigation { get; private set; }

        public INavigationView? NavigationControl { get; private set; }

        public INavigationView GetNavigationControl()
        {
            return NavigationControl!;
        }

        public bool GoBack()
        {
            return false;
        }

        public bool Navigate(Type pageType)
        {
            LastNavigationPageType = pageType;
            LastNavigationData = null;
            LastUsedHierarchyNavigation = false;
            return true;
        }

        public bool Navigate(Type pageType, object? dataContext)
        {
            LastNavigationPageType = pageType;
            LastNavigationData = dataContext;
            LastUsedHierarchyNavigation = false;
            return true;
        }

        public bool Navigate(string pageIdOrTargetTag)
        {
            return true;
        }

        public bool Navigate(string pageIdOrTargetTag, object? dataContext)
        {
            return true;
        }

        public bool NavigateWithHierarchy(Type pageType)
        {
            LastNavigationPageType = pageType;
            LastNavigationData = null;
            LastUsedHierarchyNavigation = true;
            return true;
        }

        public bool NavigateWithHierarchy(Type pageType, object? dataContext)
        {
            LastNavigationPageType = pageType;
            LastNavigationData = dataContext;
            LastUsedHierarchyNavigation = true;
            return true;
        }

        public void SetNavigationControl(INavigationView navigation)
        {
            NavigationControl = navigation;
        }
    }
}
