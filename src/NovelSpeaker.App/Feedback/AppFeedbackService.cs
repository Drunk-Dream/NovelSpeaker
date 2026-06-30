using System;
using System.Threading;
using System.Threading.Tasks;

namespace NovelSpeaker.App.Feedback;

public sealed class AppFeedbackService : IAppFeedbackService
{
    private readonly IAppDialogService _dialogService;
    private readonly IAppNotificationService _notificationService;
    private readonly IExceptionProjector _exceptionProjector;

    public AppFeedbackService(
        IAppDialogService dialogService,
        IAppNotificationService notificationService,
        IExceptionProjector exceptionProjector)
    {
        _dialogService = dialogService;
        _notificationService = notificationService;
        _exceptionProjector = exceptionProjector;
    }

    public ProjectedUiError Project(Exception exception)
    {
        return _exceptionProjector.Project(exception);
    }

    public void ShowProjectedNotification(string title, ProjectedUiError projected)
    {
        ArgumentNullException.ThrowIfNull(projected);

        if (projected.IsSilent)
        {
            return;
        }

        switch (projected.Severity)
        {
            case UiMessageSeverity.Warning:
                _notificationService.ShowWarning(title, projected.UserMessage);
                break;
            case UiMessageSeverity.Error:
                _notificationService.ShowError(title, projected.UserMessage);
                break;
            default:
                _notificationService.ShowSuccess(title, projected.UserMessage);
                break;
        }
    }

    public void ShowSuccess(string title, string message)
    {
        _notificationService.ShowSuccess(title, message);
    }

    public void ShowWarning(string title, string message)
    {
        _notificationService.ShowWarning(title, message);
    }

    public Task<AppConfirmationDecision> ConfirmDeletionAsync(
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        return _dialogService.ShowConfirmationAsync(
            title,
            message,
            "删除",
            "取消",
            cancellationToken);
    }
}
