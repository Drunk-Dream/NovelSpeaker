using System.Windows.Input;

namespace NovelSpeaker.App.Shell.Input;

/// <summary>
/// Maps shell keyboard input to user actions without letting pages duplicate shortcut logic.
/// </summary>
public interface IKeyboardShortcutCoordinator
{
    Task<bool> TryHandleAsync(
        Key key,
        ModifierKeys modifiers,
        KeyboardShortcutContext context,
        CancellationToken cancellationToken);
}
