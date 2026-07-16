using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class AppearanceSettingsViewModelTests
{
    [Fact]
    public async Task LoadAsync_reads_selected_theme()
    {
        var settingsService = new FakeAppSettingsService(AppSettings.Default with { Theme = "Dark" });
        var viewModel = CreateViewModel(settingsService, new FakeThemePreferenceService());

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("Dark", viewModel.SelectedTheme);
    }

    [Fact]
    public async Task SelectedTheme_change_uses_theme_preference_service()
    {
        var themeService = new FakeThemePreferenceService();
        var viewModel = CreateViewModel(new FakeAppSettingsService(AppSettings.Default), themeService);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectedTheme = "Dark";
        await Task.Delay(20);

        Assert.Equal("Dark", themeService.LastRequestedTheme);
    }

    private static AppearanceSettingsViewModel CreateViewModel(
        FakeAppSettingsService settingsService,
        FakeThemePreferenceService themePreferenceService)
    {
        return new AppearanceSettingsViewModel(
            settingsService,
            themePreferenceService,
            new FakeNavigationService(),
            new FakeFeedbackService());
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public FakeAppSettingsService(AppSettings currentSettings)
        {
            CurrentSettings = currentSettings.Normalize();
        }

        public AppSettings CurrentSettings { get; }
        public AppSettings Current => CurrentSettings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) => Task.FromResult(CurrentSettings);
    }

    private sealed class FakeThemePreferenceService : IThemePreferenceService
    {
        public string? LastRequestedTheme { get; private set; }

        public Task<ThemePreferenceChangeResult> ApplyAsync(string requestedTheme, CancellationToken cancellationToken)
        {
            LastRequestedTheme = requestedTheme;
            return Task.FromResult(new ThemePreferenceChangeResult(true, false, requestedTheme));
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);
        public void ShowProjectedNotification(string title, ProjectedUiError projected) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken) => Task.FromResult(AppConfirmationDecision.Cancel);
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => false;
        public bool Navigate(Type pageType) => true;
        public bool Navigate(Type pageType, object? dataContext) => true;
        public bool Navigate(string pageIdOrTargetTag) => true;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;
        public bool NavigateWithHierarchy(Type pageType) => true;
        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;
        public void SetNavigationControl(INavigationView navigation) { }
    }
}
