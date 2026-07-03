using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

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
        await Task.Delay(20);

        Assert.False(service.CurrentSettings.EnableLongParagraphSplitting);
    }

    private static ImportTextSettingsViewModel CreateViewModel(FakeAppSettingsService settingsService)
    {
        return new ImportTextSettingsViewModel(
            settingsService,
            new FakeNavigationService(),
            new FakeFeedbackService());
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public FakeAppSettingsService(AppSettings currentSettings)
        {
            CurrentSettings = currentSettings.Normalize();
        }

        public AppSettings CurrentSettings { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(CurrentSettings);

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            CurrentSettings = (CurrentSettings with
            {
                BookFileNameTemplate = update.BookFileNameTemplate ?? CurrentSettings.BookFileNameTemplate,
                EnableLongParagraphSplitting = update.EnableLongParagraphSplitting ?? CurrentSettings.EnableLongParagraphSplitting,
                LongParagraphThreshold = update.LongParagraphThreshold ?? CurrentSettings.LongParagraphThreshold
            }).Normalize();
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
