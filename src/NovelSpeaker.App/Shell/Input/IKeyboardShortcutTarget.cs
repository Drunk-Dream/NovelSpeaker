namespace NovelSpeaker.App.Shell.Input;

/// <summary>
/// Handles shell shortcuts for the page that is currently activated.
/// </summary>
public interface IKeyboardShortcutTarget
{
    Task<bool> HandleKeyboardShortcutAsync(
        KeyboardShortcutAction action,
        string? argument,
        CancellationToken cancellationToken);
}
