namespace NovelSpeaker.App.Input;

/// <summary>
/// Owns the desktop file-picker boundary used to start a TXT import.
/// </summary>
public interface ITextFilePicker
{
    Task<string?> PickSingleTextFileAsync(CancellationToken cancellationToken);
}
