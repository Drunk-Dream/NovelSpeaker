using System.Windows.Input;

namespace NovelSpeaker.App.Input;

public static class KeyboardShortcutPolicy
{
    public static KeyboardShortcutAction? Resolve(Key key, ModifierKeys modifiers, KeyboardShortcutContext context)
    {
        if (context.IsTextEditing || context.IsTransientUiOpen)
        {
            return null;
        }

        if (key == Key.O && modifiers == ModifierKeys.Control)
        {
            return KeyboardShortcutAction.ImportTextFile;
        }

        if (key == Key.OemComma && modifiers == ModifierKeys.Control)
        {
            return KeyboardShortcutAction.OpenSettings;
        }

        if ((key == Key.Left && modifiers == ModifierKeys.Alt) ||
            (key == Key.Escape && modifiers == ModifierKeys.None))
        {
            return KeyboardShortcutAction.NavigateBack;
        }

        if (!context.IsPlayerPageActive)
        {
            return null;
        }

        return (key, modifiers) switch
        {
            (Key.Space, ModifierKeys.None) => KeyboardShortcutAction.TogglePlayback,
            (Key.Left, ModifierKeys.Control) => KeyboardShortcutAction.PreviousSegment,
            (Key.Right, ModifierKeys.Control) => KeyboardShortcutAction.NextSegment,
            (Key.Left, ModifierKeys.Control | ModifierKeys.Shift) => KeyboardShortcutAction.PreviousChapter,
            (Key.Right, ModifierKeys.Control | ModifierKeys.Shift) => KeyboardShortcutAction.NextChapter,
            _ => null
        };
    }
}
