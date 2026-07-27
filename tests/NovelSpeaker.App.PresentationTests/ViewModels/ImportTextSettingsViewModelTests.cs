using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Common;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class ImportTextSettingsViewModelTests
{
    [Fact]
    public async Task CommitBookFileNameTemplateAsync_preserves_empty_template()
    {
        var service = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.BookFileNameTemplateText = "   ";

        await viewModel.CommitBookFileNameTemplateAsync(CancellationToken.None);

        Assert.Equal(string.Empty, viewModel.BookFileNameTemplateText);
        Assert.Equal(string.Empty, service.CurrentSettings.BookFileNameTemplate);
    }

    [Fact]
    public async Task CommitLongParagraphThresholdAsync_normalizes_threshold()
    {
        var service = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.LongParagraphThresholdText = "25";

        await viewModel.CommitLongParagraphThresholdAsync(CancellationToken.None);

        Assert.Equal("50", viewModel.LongParagraphThresholdText);
        Assert.Equal(50, service.CurrentSettings.LongParagraphThreshold);
    }

    [Fact]
    public async Task EnableLongParagraphSplitting_change_saves_immediately()
    {
        var service = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.EnableLongParagraphSplitting = false;
        await service.UpdateCompleted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(service.CurrentSettings.EnableLongParagraphSplitting);
    }

    [Fact]
    public async Task BookFileNameTemplateText_change_debounces_and_saves_latest_value()
    {
        var timeProvider = new ManualTimeProvider();
        var service = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(service, timeProvider);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.BookFileNameTemplateText = "{title}";
        viewModel.BookFileNameTemplateText = "{author}-{title}";

        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        await service.UpdateCompleted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("{author}-{title}", service.CurrentSettings.BookFileNameTemplate);
        Assert.Equal("{author}-{title}", viewModel.BookFileNameTemplateText);
    }

    private static ImportTextSettingsViewModel CreateViewModel(
        FakeAppSettingsService settingsService,
        TimeProvider? timeProvider = null)
    {
        return new ImportTextSettingsViewModel(
            settingsService,
            new FakeNavigationService(),
            new FakeFeedbackService(),
            timeProvider);
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public FakeAppSettingsService(AppSettings currentSettings)
        {
            CurrentSettings = currentSettings.Normalize();
        }

        public AppSettings CurrentSettings { get; private set; }
        public AppSettings Current => CurrentSettings;
        public Task UpdateCompleted => _updateCompleted.Task;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        private readonly TaskCompletionSource _updateCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            CurrentSettings = (CurrentSettings with
            {
                BookFileNameTemplate = update.BookFileNameTemplate ?? CurrentSettings.BookFileNameTemplate,
                EnableLongParagraphSplitting = update.EnableLongParagraphSplitting ?? CurrentSettings.EnableLongParagraphSplitting,
                LongParagraphThreshold = update.LongParagraphThreshold ?? CurrentSettings.LongParagraphThreshold
            }).Normalize();
            _updateCompleted.TrySetResult();
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
