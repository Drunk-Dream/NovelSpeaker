using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Features.GeneralSettings;
using NovelSpeaker.App.Features.ImportTextSettings;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class SettingsSaveLifecycleTests
{
    [Fact]
    public async Task Debounce_and_explicit_commit_save_latest_value_without_rescheduling_normalized_text()
    {
        var store = new ControlledStore();
        using var settings = new AppSettingsService(store, AppSettings.Default);
        var time = new ManualTimeProvider();
        var feedback = new Feedback();
        using var owner = new PageActivationController();
        var activation = owner.Activate();
        var viewModel = new ImportTextSettingsViewModel(settings, new Navigator(), feedback, time);
        viewModel.Activate(activation);
        await viewModel.LoadAsync(activation.CancellationToken);

        viewModel.LongParagraphThresholdText = "800";
        viewModel.LongParagraphThresholdText = "900";
        time.Advance(TimeSpan.FromMilliseconds(500));
        await activation.WaitForPendingOperationsAsync();
        Assert.Equal(900, settings.Current.LongParagraphThreshold);

        viewModel.LongParagraphThresholdText = "1000";
        await viewModel.CommitLongParagraphThresholdAsync(CancellationToken.None);
        await activation.WaitForPendingOperationsAsync();
        Assert.Equal(1000, settings.Current.LongParagraphThreshold);
        Assert.Equal(0, time.PendingTimerCount);
        Assert.Empty(feedback.Failures);
    }

    [Fact]
    public async Task Replaced_save_failure_and_page_leave_do_not_overwrite_or_notify_new_activation()
    {
        var store = new ControlledStore { BlockNextSave = true };
        using var settings = new AppSettingsService(store, AppSettings.Default);
        var feedback = new Feedback();
        using var owner = new PageActivationController();
        var activation = owner.Activate();
        var viewModel = new ImportTextSettingsViewModel(settings, new Navigator(), feedback, new ManualTimeProvider());
        viewModel.Activate(activation);
        await viewModel.LoadAsync(activation.CancellationToken);

        viewModel.LongParagraphThresholdText = "800";
        var oldSave = viewModel.CommitLongParagraphThresholdAsync(CancellationToken.None);
        await store.Started.Task;
        viewModel.LongParagraphThresholdText = "900";
        var newSave = viewModel.CommitLongParagraphThresholdAsync(CancellationToken.None);
        store.Release.SetException(new IOException("late failure"));
        await Task.WhenAll(oldSave, newSave);
        Assert.Equal(900, settings.Current.LongParagraphThreshold);
        Assert.Equal("900", viewModel.LongParagraphThresholdText);
        Assert.Empty(feedback.Failures);

        viewModel.LongParagraphThresholdText = "1100";
        owner.Deactivate();
        await activation.WaitForPendingOperationsAsync();
        var next = owner.Activate();
        viewModel.Activate(next);
        await viewModel.LoadAsync(next.CancellationToken);
        Assert.Equal("900", viewModel.LongParagraphThresholdText);
        Assert.Empty(feedback.Failures);
    }

    [Fact]
    public async Task Replaced_toggle_failure_is_silent_and_current_failure_reverts_with_one_notification()
    {
        var store = new ControlledStore { BlockNextSave = true };
        using var settings = new AppSettingsService(store, AppSettings.Default);
        var feedback = new Feedback();
        using var owner = new PageActivationController();
        var activation = owner.Activate();
        var viewModel = new GeneralSettingsViewModel(settings, new Navigator(), feedback);
        viewModel.Activate(activation);
        await viewModel.LoadAsync(activation.CancellationToken);
        viewModel.StartMinimizedToTray = true;
        await store.Started.Task;
        viewModel.StartMinimizedToTray = false;
        store.Release.SetException(new IOException("late failure"));
        await activation.WaitForPendingOperationsAsync();
        Assert.False(settings.Current.StartMinimizedToTray);
        Assert.Empty(feedback.Failures);

        store.FailSave = true;
        viewModel.StartMinimizedToTray = true;
        await activation.WaitForPendingOperationsAsync();
        Assert.False(viewModel.StartMinimizedToTray);
        Assert.Single(feedback.Failures);
    }

    private sealed class ControlledStore : IAppSettingsStore
    {
        public bool BlockNextSave { get; set; }
        public bool FailSave { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(AppSettings.Default);
        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (BlockNextSave)
            {
                BlockNextSave = false;
                Started.TrySetResult();
                await Release.Task; // Deliberately simulate a store that completes after cancellation.
            }
            if (FailSave) throw new IOException("current failure");
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class Navigator : IAppNavigator
    {
        public AppRoute CurrentRoute => AppRoutes.Settings;
        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false) => Task.FromResult(true);
        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) => Task.FromResult(true);
    }

    private sealed class Feedback : IAppFeedbackService
    {
        public List<string> Failures { get; } = [];
        public ProjectedUiError Project(Exception exception) => new("save failed", UiMessageSeverity.Error, false);
        public void ShowProjectedNotification(string title, ProjectedUiError projected) => Failures.Add(title);
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) => Failures.Add(title);
        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken) =>
            Task.FromResult(AppConfirmationDecision.Cancel);
    }
}
