using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.UnitTests.Common;
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
        var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Paused,
            BookId = "book-1",
            SpeakSpeed = 10
        });
        var viewModel = CreateViewModel(service, playbackCoordinator: coordinator);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DefaultSpeakSpeedText = "99";

        await viewModel.CommitDefaultSpeakSpeedAsync(CancellationToken.None);

        Assert.Equal(AppSettings.MaxSpeakSpeed.ToString(), viewModel.DefaultSpeakSpeedText);
        Assert.Equal(AppSettings.MaxSpeakSpeed, service.CurrentSettings.DefaultSpeakSpeed);
        Assert.Equal(AppSettings.MaxSpeakSpeed, coordinator.LastChangedSpeakSpeed);
    }

    [Fact]
    public async Task CommitDefaultSpeakSpeedAsync_without_playback_session_only_updates_global_speed()
    {
        var service = new FakeAppSettingsService(AppSettings.Default);
        var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle);
        var viewModel = CreateViewModel(service, playbackCoordinator: coordinator);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DefaultSpeakSpeedText = "12";

        await viewModel.CommitDefaultSpeakSpeedAsync(CancellationToken.None);

        Assert.Equal(12, service.CurrentSettings.DefaultSpeakSpeed);
        Assert.Null(coordinator.LastChangedSpeakSpeed);
    }

    [Fact]
    public async Task DefaultSpeakSpeedText_change_debounces_global_update_and_audio_regeneration()
    {
        var timeProvider = new ManualTimeProvider();
        var service = new FakeAppSettingsService(AppSettings.Default);
        var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Playing,
            BookId = "book-1",
            SpeakSpeed = 10
        });
        var viewModel = CreateViewModel(
            service,
            playbackCoordinator: coordinator,
            timeProvider: timeProvider);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.DefaultSpeakSpeedText = "11";
        viewModel.DefaultSpeakSpeedText = "12";

        timeProvider.Advance(TimeSpan.FromMilliseconds(499));
        await Task.Yield();
        Assert.Equal(10, service.CurrentSettings.DefaultSpeakSpeed);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await coordinator.WaitForSpeedChangeAsync();

        Assert.Equal(12, service.CurrentSettings.DefaultSpeakSpeed);
        Assert.Equal(12, coordinator.LastChangedSpeakSpeed);
    }

    [Fact]
    public async Task Leaving_page_cancels_pending_debounced_setting_operation()
    {
        var timeProvider = new ManualTimeProvider();
        var service = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(service, timeProvider: timeProvider);
        using var activationController = new PageActivationController();
        var activation = activationController.Activate();
        viewModel.Activate(activation);
        activation.Register(viewModel.Deactivate);
        await viewModel.LoadAsync(activation.CancellationToken);

        viewModel.DefaultSpeakSpeedText = "12";
        activationController.Deactivate();
        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        await activation.WaitForPendingOperationsAsync();

        Assert.Equal(AppSettings.DefaultSpeakSpeedValue, service.CurrentSettings.DefaultSpeakSpeed);
    }

    [Fact]
    public async Task Late_debounced_result_from_old_activation_cannot_update_reentered_page()
    {
        var timeProvider = new ManualTimeProvider();
        var service = new FakeAppSettingsService(AppSettings.Default)
        {
            DelayUpdates = true
        };
        var playback = new FakePlaybackCoordinator(PlaybackSnapshot.Idle);
        var viewModel = CreateViewModel(
            service,
            playbackCoordinator: playback,
            timeProvider: timeProvider);
        using var activationController = new PageActivationController();
        var oldActivation = activationController.Activate();
        viewModel.Activate(oldActivation);
        oldActivation.Register(viewModel.Deactivate);
        await viewModel.LoadAsync(oldActivation.CancellationToken);

        viewModel.DefaultSpeakSpeedText = "12";
        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        await service.UpdateStarted;

        var newActivation = activationController.Activate();
        viewModel.Activate(newActivation);
        newActivation.Register(viewModel.Deactivate);
        await viewModel.LoadAsync(newActivation.CancellationToken);
        service.CompleteUpdate();
        await oldActivation.WaitForPendingOperationsAsync();

        Assert.Equal(AppSettings.DefaultSpeakSpeedValue.ToString(), viewModel.DefaultSpeakSpeedText);
        Assert.Null(playback.LastChangedSpeakSpeed);
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
        FakeFeedbackService? feedbackService = null,
        IPlaybackSession? playbackCoordinator = null,
        TimeProvider? timeProvider = null)
    {
        return new PlaybackSettingsViewModel(
            settingsService,
            playbackCoordinator ?? new FakePlaybackCoordinator(PlaybackSnapshot.Idle),
            navigationService ?? new FakeNavigationService(),
            feedbackService ?? new FakeFeedbackService(),
            timeProvider);
    }

    private sealed class FakePlaybackCoordinator(PlaybackSnapshot snapshot) : IPlaybackSession
    {
        public PlaybackSnapshot CurrentSnapshot { get; private set; } = snapshot;
        public int? LastChangedSpeakSpeed { get; private set; }
        private readonly TaskCompletionSource _speedChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken)
        {
            LastChangedSpeakSpeed = speakSpeed;
            CurrentSnapshot = CurrentSnapshot with { SpeakSpeed = speakSpeed };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            _speedChanged.TrySetResult();
            return Task.CompletedTask;
        }

        public Task WaitForSpeedChangeAsync() => _speedChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PauseAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResumeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task NextSegmentAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task NextChapterAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public FakeAppSettingsService(AppSettings currentSettings)
        {
            CurrentSettings = currentSettings.Normalize();
        }

        public AppSettings CurrentSettings { get; private set; }
        public AppSettings Current => CurrentSettings;
        public bool DelayUpdates { get; init; }
        public Task UpdateStarted => _updateStarted.Task;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        private readonly TaskCompletionSource _updateStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _updateCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            if (DelayUpdates)
            {
                _updateStarted.TrySetResult();
                await _updateCompletion.Task;
            }

            CurrentSettings = CurrentSettings with
            {
                DefaultSpeakSpeed = update.DefaultSpeakSpeed ?? CurrentSettings.DefaultSpeakSpeed,
                PrefetchCount = update.PrefetchCount ?? CurrentSettings.PrefetchCount
            };
            CurrentSettings = CurrentSettings.Normalize();
            return CurrentSettings;
        }

        public void CompleteUpdate()
        {
            _updateCompletion.TrySetResult();
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

    private sealed class FakeNavigationService : ITestNavigationService
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
