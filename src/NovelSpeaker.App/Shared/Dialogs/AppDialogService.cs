using System.Threading;
using System.Threading.Tasks;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shared.Dialogs;

public sealed class AppDialogService : IAppDialogService
{
    private readonly IContentDialogService _contentDialogService;

    public AppDialogService(IContentDialogService contentDialogService)
    {
        _contentDialogService = contentDialogService;
    }

    public async Task<AppConfirmationDecision> ShowConfirmationAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText,
        CancellationToken cancellationToken)
    {
        if (_contentDialogService.GetDialogHostEx() is null)
        {
            return ShowConfirmationFallback(title, message) == global::System.Windows.MessageBoxResult.OK
                ? AppConfirmationDecision.Confirm
                : AppConfirmationDecision.Cancel;
        }

        var dialog = AppDialogVisuals.Create(
            title,
            AppDialogVisuals.Wrap(AppDialogVisuals.CreateMessage(message)),
            primaryButtonText,
            null,
            closeButtonText);
        var result = await _contentDialogService.ShowAsync(dialog, cancellationToken);
        return result == ContentDialogResult.Primary
            ? AppConfirmationDecision.Confirm
            : AppConfirmationDecision.Cancel;
    }

    public async Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
        string title,
        string message,
        string saveButtonText,
        string discardButtonText,
        string cancelButtonText,
        CancellationToken cancellationToken)
    {
        if (_contentDialogService.GetDialogHostEx() is null)
        {
            return ShowUnsavedChangesFallback(title, message) switch
            {
                global::System.Windows.MessageBoxResult.Yes => UnsavedChangesDecision.Save,
                global::System.Windows.MessageBoxResult.No => UnsavedChangesDecision.Discard,
                _ => UnsavedChangesDecision.Cancel
            };
        }

        var dialog = AppDialogVisuals.Create(
            title,
            AppDialogVisuals.Wrap(AppDialogVisuals.CreateMessage(message)),
            saveButtonText,
            discardButtonText,
            cancelButtonText);
        var result = await _contentDialogService.ShowAsync(dialog, cancellationToken);

        return result switch
        {
            ContentDialogResult.Primary => UnsavedChangesDecision.Save,
            ContentDialogResult.Secondary => UnsavedChangesDecision.Discard,
            _ => UnsavedChangesDecision.Cancel
        };
    }

    private static global::System.Windows.MessageBoxResult ShowConfirmationFallback(string title, string message)
    {
        return global::System.Windows.MessageBox.Show(
            message,
            title,
            global::System.Windows.MessageBoxButton.OKCancel,
            global::System.Windows.MessageBoxImage.Warning);
    }

    private static global::System.Windows.MessageBoxResult ShowUnsavedChangesFallback(string title, string message)
    {
        return global::System.Windows.MessageBox.Show(
            message,
            title,
            global::System.Windows.MessageBoxButton.YesNoCancel,
            global::System.Windows.MessageBoxImage.Warning);
    }
}
