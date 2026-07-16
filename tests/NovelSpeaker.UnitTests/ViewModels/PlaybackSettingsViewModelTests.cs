using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class PlaybackSettingsViewModelTests
{
    [Fact]
    public async Task LoadAsync_reads_saved_values()
    {
        var service = new FakeAppSettingsService(AppSettings.Default with { DefaultSpeakSpeed = 14, PrefetchCount = 1 });
        var viewModel = CreateViewModel(service);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("14", viewModel.DefaultSpeakSpeedText);
        Assert.Equal("1", viewModel.PrefetchCountText);
    }

    [Fact]
    public async Task CommitDefaultSpeakSpeedAsync_normalizes_and_updates_text()
    {
        var service = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DefaultSpeakSpeedText = "99";

        await viewModel.CommitDefaultSpeakSpeedAsync(CancellationToken.None);

        Assert.Equal(AppSettings.MaxSpeakSpeed.ToString(), viewModel.DefaultSpeakSpeedText);
        Assert.Equal(AppSettings.MaxSpeakSpeed, service.CurrentSettings.DefaultSpeakSpeed);
    }

    [Fact]
    public async Task CommitPrefetchCountAsync_rejects_non_integer_input()
    {
        var service = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.PrefetchCountText = "abc";

        await viewModel.CommitPrefetchCountAsync(CancellationToken.None);

        Assert.Contains("请输入", viewModel.PrefetchCountErrorText);
        Assert.Equal(AppSettings.DefaultPrefetchCountValue, service.CurrentSettings.PrefetchCount);
    }

    [Fact]
    public void OpenTtsRulesCommand_navigates_to_tts_rules_page()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = CreateViewModel(new FakeAppSettingsService(AppSettings.Default), navigationService);

        viewModel.OpenTtsRulesCommand.Execute(null);

        Assert.Equal(typeof(TtsRulesPage), navigationService.LastNavigationPageType);
    }

    private static PlaybackSettingsViewModel CreateViewModel(
        FakeAppSettingsService settingsService,
        FakeNavigationService? navigationService = null,
        FakeFeedbackService? feedbackService = null)
    {
        return new PlaybackSettingsViewModel(
            settingsService,
            navigationService ?? new FakeNavigationService(),
            feedbackService ?? new FakeFeedbackService());
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public FakeAppSettingsService(AppSettings currentSettings)
        {
            CurrentSettings = currentSettings.Normalize();
        }

        public AppSettings CurrentSettings { get; private set; }
        public AppSettings Current => CurrentSettings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            CurrentSettings = CurrentSettings with
            {
                DefaultSpeakSpeed = update.DefaultSpeakSpeed ?? CurrentSettings.DefaultSpeakSpeed,
                PrefetchCount = update.PrefetchCount ?? CurrentSettings.PrefetchCount
            };
            CurrentSettings = CurrentSettings.Normalize();
            return Task.FromResult(CurrentSettings);
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
        public Type? LastNavigationPageType { get; private set; }
        public INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => false;
        public bool Navigate(Type pageType) => true;
        public bool Navigate(Type pageType, object? dataContext) => true;
        public bool Navigate(string pageIdOrTargetTag) => true;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;
        public bool NavigateWithHierarchy(Type pageType)
        {
            LastNavigationPageType = pageType;
            return true;
        }
        public bool NavigateWithHierarchy(Type pageType, object? dataContext)
        {
            LastNavigationPageType = pageType;
            return true;
        }
        public void SetNavigationControl(INavigationView navigation) { }
    }
}
