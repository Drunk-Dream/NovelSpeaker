using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Diagnostics;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class DiagnosticsAboutViewModelTests
{
    [Fact]
    public async Task LoadAsync_populates_snapshot_and_log_level()
    {
        var viewModel = CreateViewModel(
            new FakeDiagnosticsService(),
            new FakeAppSettingsService(AppSettings.Default with { LogLevel = "Warning" }));

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("NovelSpeaker", viewModel.AppName);
        Assert.Equal("4", viewModel.DatabaseSchemaVersionText);
        Assert.Equal("Warning", viewModel.SelectedLogLevel);
    }

    [Fact]
    public async Task SelectedLogLevel_change_saves_immediately()
    {
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(new FakeDiagnosticsService(), settingsService);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectedLogLevel = "Error";
        await Task.Delay(20);

        Assert.Equal("Error", settingsService.CurrentSettings.LogLevel);
    }

    [Fact]
    public async Task OpenLogsDirectoryCommand_failure_notifies_user()
    {
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(
            new FakeDiagnosticsService { OpenException = new InvalidOperationException("open failed") },
            new FakeAppSettingsService(AppSettings.Default),
            feedbackService: feedback);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.OpenLogsDirectoryCommand.ExecuteAsync(null);

        Assert.Equal("打开日志目录失败", feedback.LastTitle);
    }

    [Fact]
    public async Task CopyRedactedSummaryCommand_copies_service_summary_and_notifies_user()
    {
        var clipboard = new FakeClipboardService();
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(
            new FakeDiagnosticsService(),
            new FakeAppSettingsService(AppSettings.Default),
            feedbackService: feedback,
            clipboardService: clipboard);

        await viewModel.CopyRedactedSummaryCommand.ExecuteAsync(null);

        Assert.Equal("诊断摘要", clipboard.Text);
        Assert.Equal("诊断摘要已复制", feedback.LastTitle);
    }

    private static DiagnosticsAboutViewModel CreateViewModel(
        FakeDiagnosticsService diagnosticsService,
        FakeAppSettingsService settingsService,
        FakeFeedbackService? feedbackService = null,
        FakeClipboardService? clipboardService = null)
    {
        return new DiagnosticsAboutViewModel(
            diagnosticsService,
            settingsService,
            clipboardService ?? new FakeClipboardService(),
            new FakeNavigationService(),
            feedbackService ?? new FakeFeedbackService());
    }

    private sealed class FakeDiagnosticsService : IAppDiagnosticsService
    {
        public Exception? OpenException { get; set; }

        public Task<AppDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AppDiagnosticsSnapshot(
                "NovelSpeaker",
                "1.2.3",
                "Windows 10/11 桌面小说听书应用。",
                4,
                @"C:\Data",
                @"C:\Logs"));
        }

        public Task OpenLogsDirectoryAsync(CancellationToken cancellationToken)
        {
            if (OpenException is not null)
            {
                throw OpenException;
            }

            return Task.CompletedTask;
        }

        public Task<string> GetRedactedSummaryAsync(CancellationToken cancellationToken) => Task.FromResult("诊断摘要");

        public Task OpenThirdPartyNoticesAsync(CancellationToken cancellationToken)
        {
            if (OpenException is not null)
            {
                throw OpenException;
            }

            return Task.CompletedTask;
        }

        public Task OpenAppDataDirectoryAsync(CancellationToken cancellationToken)
        {
            if (OpenException is not null)
            {
                throw OpenException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;
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
                LogLevel = update.LogLevel ?? CurrentSettings.LogLevel
            }).Normalize();
            return Task.FromResult(CurrentSettings);
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }
        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);
        public void ShowProjectedNotification(string title, ProjectedUiError projected) => LastTitle = title;
        public void ShowSuccess(string title, string message) => LastTitle = title;
        public void ShowWarning(string title, string message) => LastTitle = title;
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
