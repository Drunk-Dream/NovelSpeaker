using Microsoft.Win32;

namespace NovelSpeaker.App.Shared.Presentation.Platform;

public sealed class WpfPresentationFileDialogService : IPresentationFileDialogService
{
    public Task<string?> PickOpenFileAsync(
        PresentationFileDialogOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new OpenFileDialog
        {
            Filter = options.Filter,
            Multiselect = false,
            FileName = options.SuggestedFileName ?? string.Empty
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> PickSaveFileAsync(
        PresentationFileDialogOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new SaveFileDialog
        {
            Filter = options.Filter,
            FileName = options.SuggestedFileName ?? string.Empty
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}
