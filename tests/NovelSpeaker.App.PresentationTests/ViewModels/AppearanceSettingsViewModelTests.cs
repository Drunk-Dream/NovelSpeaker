using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class AppearanceSettingsViewModelTests
{
    [Fact]
    public async Task Late_theme_result_from_old_activation_cannot_update_reentered_page()
    {
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var themeService = new GatedThemePreferenceService();
        var viewModel = CreateViewModel(settingsService, themeService);
        using var activationController = new PageActivationController();
        var oldActivation = activationController.Activate();
        viewModel.Activate(oldActivation);
        oldActivation.Register(viewModel.Deactivate);
        await viewModel.LoadAsync(oldActivation.CancellationToken);

        viewModel.SelectedTheme = "Dark";
        await themeService.Started;

        var newActivation = activationController.Activate();
        viewModel.Activate(newActivation);
        newActivation.Register(viewModel.Deactivate);
        await viewModel.LoadAsync(newActivation.CancellationToken);
        themeService.Complete("Dark");
        await oldActivation.WaitForPendingOperationsAsync();

        Assert.Equal(settingsService.Current.Theme, viewModel.SelectedTheme);
    }

    private static AppearanceSettingsViewModel CreateViewModel(
        FakeAppSettingsService settingsService,
        IThemePreferenceService themePreferenceService)
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

    private sealed class GatedThemePreferenceService : IThemePreferenceService
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ThemePreferenceChangeResult> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public Task<ThemePreferenceChangeResult> ApplyAsync(
            string requestedTheme,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            return _completion.Task;
        }

        public void Complete(string effectiveTheme)
        {
            _completion.TrySetResult(new ThemePreferenceChangeResult(true, false, effectiveTheme));
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
