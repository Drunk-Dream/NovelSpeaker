namespace NovelSpeaker.App.Shared.Dialogs;

/// <summary>
/// Owns the desktop file-picker boundary used to start a TXT import.
/// </summary>
public interface ITextFilePicker
{
    Task<string?> PickSingleTextFileAsync(CancellationToken cancellationToken);
}
