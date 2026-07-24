namespace NovelSpeaker.App.Shared.Presentation.Platform;

/// <summary>
/// Presents desktop open/save dialogs without exposing WPF dialog types to callers.
/// </summary>
public interface IPresentationFileDialogService
{
    Task<string?> PickOpenFileAsync(
        PresentationFileDialogOptions options,
        CancellationToken cancellationToken);

    Task<string?> PickSaveFileAsync(
        PresentationFileDialogOptions options,
        CancellationToken cancellationToken);
}
