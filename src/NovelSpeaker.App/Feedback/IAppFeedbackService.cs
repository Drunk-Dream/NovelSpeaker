using System;
using System.Threading;
using System.Threading.Tasks;

namespace NovelSpeaker.App.Feedback;

public interface IAppFeedbackService
{
    ProjectedUiError Project(Exception exception);

    void ShowProjectedNotification(string title, ProjectedUiError projected);

    void ShowSuccess(string title, string message);

    void ShowWarning(string title, string message);

    Task<AppConfirmationDecision> ConfirmDeletionAsync(
        string title,
        string message,
        CancellationToken cancellationToken);
}
