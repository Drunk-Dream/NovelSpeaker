using System.Windows;
using System.Windows.Controls;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Shared.Feedback;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.App.WpfTests.Feedback;

[Collection("WpfDispatcher")]
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
            AssertStandardDialogVisuals(dialogService.LastDialog!);
            Assert.Equal("放弃", dialogService.LastDialog!.SecondaryButtonText);
        });
    }

    [Fact]
    public void EncodingSelectionDialogService_uses_standard_dialog_content_and_input_styles()
    {
        WpfTestHost.RunInSta(() =>
        {
            var dialogService = new FakeContentDialogService
            {
                NextResult = ContentDialogResult.Primary
            };
            var service = new EncodingSelectionDialogService(dialogService);

            var selected = service.ShowAsync(
                    new EncodingSelectionPrompt(
                        "C:\\fixture.txt",
                        "fixture.txt",
                        "请选择编码。",
                        "UTF-8",
                        ["UTF-8", "GB18030"]),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.Equal("UTF-8", selected);
            AssertStandardDialogVisuals(dialogService.LastDialog!);
            var content = Assert.IsType<StackPanel>(Assert.IsType<Border>(dialogService.LastDialog!.Content).Child);
            var comboBox = Assert.IsType<ComboBox>(content.Children[2]);
            Assert.Same(global::System.Windows.Application.Current.FindResource("App.Input.ComboBox.Standard"), comboBox.Style);
        });
    }

    [Fact]
    public void ImportProgressDialogService_uses_standard_content_progress_and_cancel_styles()
    {
        WpfTestHost.RunInSta(() =>
        {
            var dialogService = new FakeContentDialogService();
            var service = new ImportProgressDialogService(dialogService, new ImmediateUiScheduler());

            var result = service.RunAsync(
                    "fixture.txt",
                    (_, _) => Task.FromResult(new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Imported)),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.Equal(LibraryImportCoordinatorStatus.Imported, result.Status);
            var dialog = dialogService.LastDialog!;
            Assert.False(dialog.IsFooterVisible);
            var surface = Assert.IsType<Border>(dialog.Content);
            Assert.Same(global::System.Windows.Application.Current.FindResource("App.Feedback.DialogContent"), surface.Style);
            var content = Assert.IsType<StackPanel>(surface.Child);
            Assert.Same(
                global::System.Windows.Application.Current.FindResource("App.Feedback.DialogMessage"),
                Assert.IsType<WpfTextBlock>(content.Children[1]).Style);
            Assert.Same(
                global::System.Windows.Application.Current.FindResource("App.Progress.Standard"),
                Assert.IsType<ProgressBar>(content.Children[2]).Style);
            Assert.Same(
                global::System.Windows.Application.Current.FindResource("App.Button.Secondary"),
                Assert.IsType<WpfButton>(content.Children[3]).Style);
        });
    }

    [Fact]
    public void ImportProgressDialogService_cancels_operation_when_host_closes_dialog()
    {
        WpfTestHost.RunInSta(() =>
        {
            var dialogService = new FakeContentDialogService
            {
                OnShow = dialog =>
                {
                    var closedEventArgs = (ContentDialogClosedEventArgs)global::System.Runtime.CompilerServices.RuntimeHelpers
                        .GetUninitializedObject(typeof(ContentDialogClosedEventArgs));
                    closedEventArgs.RoutedEvent = ContentDialog.ClosedEvent;
                    dialog.RaiseEvent(closedEventArgs);
                }
            };
            var service = new ImportProgressDialogService(dialogService, new ImmediateUiScheduler());
            var operationObservedCancellation = false;

            var result = service.RunAsync(
                    "fixture.txt",
                    (_, cancellationToken) =>
                    {
                        operationObservedCancellation = cancellationToken.IsCancellationRequested;
                        return Task.FromCanceled<LibraryImportCoordinatorResult>(cancellationToken);
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.True(operationObservedCancellation);
            Assert.Equal(LibraryImportCoordinatorStatus.Cancelled, result.Status);
        });
    }

    [Fact]
    public void ImportProgressDialogService_closes_dialog_and_preserves_operation_failure()
    {
        WpfTestHost.RunInSta(() =>
        {
            var closingWasRaised = false;
            var dialogService = new FakeContentDialogService
            {
                OnShow = dialog => dialog.Closing += (_, _) => closingWasRaised = true
            };
            var service = new ImportProgressDialogService(dialogService, new ImmediateUiScheduler());
            var expected = new InvalidOperationException("fixture failure");

            var actual = Assert.Throws<InvalidOperationException>(() =>
                service.RunAsync(
                        "fixture.txt",
                        (_, _) => Task.FromException<LibraryImportCoordinatorResult>(expected),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            Assert.Same(expected, actual);
            Assert.True(closingWasRaised);
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
            AssertStandardDialogVisuals(dialogService.LastDialog!);
            Assert.Equal("提示", snackbarService.LastTitle);
            Assert.Equal("普通警告", snackbarService.LastMessage);
            Assert.Equal(ControlAppearance.Caution, snackbarService.LastAppearance);
        });
    }

    private static void AssertStandardDialogVisuals(ContentDialog dialog)
    {
        var surface = Assert.IsType<Border>(dialog.Content);
        Assert.Same(global::System.Windows.Application.Current.FindResource("App.Feedback.DialogContent"), surface.Style);
        Assert.Equal(ContentDialogButton.Primary, dialog.DefaultButton);
        Assert.Equal(ControlAppearance.Primary, dialog.PrimaryButtonAppearance);
        Assert.Equal(ControlAppearance.Secondary, dialog.CloseButtonAppearance);
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

        public Action<ContentDialog>? OnShow { get; init; }

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
            OnShow?.Invoke(dialog);
            return Task.FromResult(NextResult);
        }
    }

    private sealed class ImmediateUiScheduler : NovelSpeaker.App.Shared.Presentation.Platform.IUiScheduler
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
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
