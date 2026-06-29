using System.Threading;
using System.Threading.Tasks;

namespace NovelSpeaker.App.Feedback;

public interface IAppDialogService
{
    Task<AppConfirmationDecision> ShowConfirmationAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText,
        CancellationToken cancellationToken);

    Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
        string title,
        string message,
        string saveButtonText,
        string discardButtonText,
        string cancelButtonText,
        CancellationToken cancellationToken);
}
