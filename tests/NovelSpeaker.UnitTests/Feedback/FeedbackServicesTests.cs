using System.Windows.Controls;
using NovelSpeaker.App.Feedback;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.Feedback;

public sealed class FeedbackServicesTests
{
    [Fact]
    public void AppDialogService_maps_confirmation_and_unsaved_changes_results()
    {
        WpfTestHost.RunInSta(() =>
        {
            var dialogService = new FakeContentDialogService
            {
                NextResult = ContentDialogResult.Primary
            };
            var appDialogService = new AppDialogService(dialogService);

            var confirm = appDialogService.ShowConfirmationAsync("title", "message", "确认", "取消", CancellationToken.None).GetAwaiter().GetResult();
            dialogService.NextResult = ContentDialogResult.Secondary;
            var unsaved = appDialogService.ShowUnsavedChangesAsync("title", "message", "保存", "放弃", "取消", CancellationToken.None).GetAwaiter().GetResult();

            Assert.Equal(AppConfirmationDecision.Confirm, confirm);
            Assert.Equal(UnsavedChangesDecision.Discard, unsaved);
        });
    }

    [Fact]
    public void AppNotificationService_routes_messages_to_snackbar_service()
    {
        WpfTestHost.RunInSta(() =>
        {
            var snackbarService = new FakeSnackbarService();
            var notifications = new AppNotificationService(snackbarService);

            notifications.ShowError("标题", "内容");

            Assert.Equal("标题", snackbarService.LastTitle);
            Assert.Equal("内容", snackbarService.LastMessage);
            Assert.Equal(ControlAppearance.Danger, snackbarService.LastAppearance);
        });
    }

    [Fact]
    public void ExceptionProjector_hides_unexpected_error_details_and_silences_cancellation()
    {
        var projector = new ExceptionProjector();

        var canceled = projector.Project(new OperationCanceledException());
        var unexpected = projector.Project(new Exception("secret path"));

        Assert.True(canceled.IsSilent);
        Assert.Equal("操作失败，请稍后重试。", unexpected.UserMessage);
    }

    private sealed class FakeContentDialogService : IContentDialogService
    {
        private readonly ContentPresenter _presenter = new();
        private readonly ContentDialogHost _host = new();

        public ContentDialogResult NextResult { get; set; }

        public void SetDialogHost(ContentPresenter contentPresenter)
        {
        }

        public void SetContentPresenter(ContentPresenter contentPresenter)
        {
        }

        public void SetDialogHost(ContentDialogHost contentDialogHost)
        {
        }

        public ContentPresenter GetDialogHost() => _presenter;

        public ContentPresenter GetContentPresenter() => _presenter;

        public ContentDialogHost GetDialogHostEx() => _host;

        public Task<ContentDialogResult> ShowAsync(ContentDialog dialog, CancellationToken cancellationToken)
        {
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeSnackbarService : ISnackbarService
    {
        public TimeSpan DefaultTimeOut { get; set; }

        private readonly SnackbarPresenter _presenter = new();

        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public ControlAppearance LastAppearance { get; private set; }

        public void SetSnackbarPresenter(SnackbarPresenter contentPresenter)
        {
        }

        public SnackbarPresenter GetSnackbarPresenter() => _presenter;

        public void Show(string title, string message, ControlAppearance appearance, IconElement? icon, TimeSpan timeout)
        {
            LastTitle = title;
            LastMessage = message;
            LastAppearance = appearance;
        }
    }
}
