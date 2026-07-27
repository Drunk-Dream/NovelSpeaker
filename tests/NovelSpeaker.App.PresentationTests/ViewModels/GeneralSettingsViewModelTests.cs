using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Features.GeneralSettings;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class GeneralSettingsViewModelTests
{
    [Fact]
    public async Task Load_projects_saved_desktop_lifecycle_preferences()
    {
        var settings = new FakeSettingsService(
            AppSettings.Default with
            {
                MainWindowCloseBehavior = MainWindowCloseBehavior.AskEveryTime,
                StartMinimizedToTray = true
            });
        var viewModel = CreateViewModel(settings);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(MainWindowCloseBehavior.AskEveryTime, viewModel.SelectedCloseBehavior?.Value);
        Assert.True(viewModel.StartMinimizedToTray);
        Assert.Empty(settings.Updates);
    }

    [Fact]
    public async Task User_changes_are_immediately_persisted_through_settings_service()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settings);
        using var activationController = new PageActivationController();
        var activation = activationController.Activate();
        viewModel.Activate(activation);
        activation.Register(viewModel.Deactivate);
        await viewModel.LoadAsync(activation.CancellationToken);

        viewModel.SelectedCloseBehavior = viewModel.CloseBehaviorOptions.Single(
            option => option.Value == MainWindowCloseBehavior.ExitApplication);
        viewModel.StartMinimizedToTray = true;
        await activation.WaitForPendingOperationsAsync();

        Assert.Contains(
            settings.Updates,
            update => update.MainWindowCloseBehavior == MainWindowCloseBehavior.ExitApplication);
        Assert.Contains(settings.Updates, update => update.StartMinimizedToTray == true);
    }

    private static GeneralSettingsViewModel CreateViewModel(FakeSettingsService settings)
    {
        return new GeneralSettingsViewModel(
            settings,
            new FakeNavigator(),
            new FakeFeedbackService());
    }

    private sealed class FakeSettingsService(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = settings;
        public List<AppSettingsUpdate> Updates { get; } = [];

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(
            AppSettingsUpdate update,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Updates.Add(update);
            Current = Current with
            {
                MainWindowCloseBehavior =
                    update.MainWindowCloseBehavior ?? Current.MainWindowCloseBehavior,
                StartMinimizedToTray =
                    update.StartMinimizedToTray ?? Current.StartMinimizedToTray
            };
            return Task.FromResult(Current);
        }
    }

    private sealed class FakeNavigator : IAppNavigator
    {
        public Task<bool> NavigateAsync(
            AppRoute route,
            CancellationToken cancellationToken,
            bool bypassGuard = false) => Task.FromResult(true);

        public Task<bool> GoBackAsync(
            CancellationToken cancellationToken,
            bool bypassGuard = false) => Task.FromResult(true);
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception) =>
            new("保存失败", UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken) =>
            Task.FromResult(AppConfirmationDecision.Cancel);
    }
}
