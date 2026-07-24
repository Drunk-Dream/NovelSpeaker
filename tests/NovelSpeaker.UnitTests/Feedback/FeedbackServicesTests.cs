using System.Windows.Controls;
using NovelSpeaker.App.Shared.Feedback;
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

            notifications.ShowWarning("标题", "内容");

            Assert.Equal("标题", snackbarService.LastTitle);
            Assert.Equal("内容", snackbarService.LastMessage);
            Assert.Equal(ControlAppearance.Caution, snackbarService.LastAppearance);
        });
    }

    [Fact]
    public void AppFeedbackService_confirms_deletion_and_routes_projected_notifications()
    {
        WpfTestHost.RunInSta(() =>
        {
            var dialogService = new FakeContentDialogService
            {
                NextResult = ContentDialogResult.Primary
            };
            var snackbarService = new FakeSnackbarService();
            var feedbackService = new AppFeedbackService(
                new AppDialogService(dialogService),
                new AppNotificationService(snackbarService),
                new ExceptionProjector());

            var decision = feedbackService
                .ConfirmDeletionAsync("删除规则", "将删除规则“示例规则”。此操作不可撤销。", CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var projected = feedbackService.Project(new InvalidOperationException("规则正在使用中。"));
            feedbackService.ShowProjectedNotification("规则删除失败", projected);
            feedbackService.ShowWarning("提示", "普通警告");

            Assert.Equal(AppConfirmationDecision.Confirm, decision);
            Assert.Equal("删除", dialogService.LastDialog?.PrimaryButtonText);
            Assert.Equal("取消", dialogService.LastDialog?.CloseButtonText);
            Assert.Equal("提示", snackbarService.LastTitle);
            Assert.Equal("普通警告", snackbarService.LastMessage);
            Assert.Equal(ControlAppearance.Caution, snackbarService.LastAppearance);
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

    [Fact]
    public void ExceptionProjector_does_not_expose_invalid_operation_messages()
    {
        var projected = new ExceptionProjector().Project(new InvalidOperationException("Token=secret"));

        Assert.Equal("当前操作无法完成，请检查相关设置后重试。", projected.UserMessage);
        Assert.DoesNotContain("secret", projected.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeContentDialogService : IContentDialogService
    {
        private readonly ContentPresenter _presenter = new();
        private readonly ContentDialogHost _host = new();

        public ContentDialogResult NextResult { get; set; }

        public ContentDialog? LastDialog { get; private set; }

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
            LastDialog = dialog;
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
