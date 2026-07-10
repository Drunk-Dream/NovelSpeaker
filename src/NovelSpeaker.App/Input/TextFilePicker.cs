using Microsoft.Win32;

namespace NovelSpeaker.App.Input;

public sealed class TextFilePicker : ITextFilePicker
{
    public Task<string?> PickSingleTextFileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = false
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}
