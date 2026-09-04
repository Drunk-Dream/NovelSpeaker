namespace NovelSpeaker.App.Shell.Input;

/// <summary>
/// Tracks the current page activation target for shell shortcuts.
/// It stores no page state and never outlives the activation registration.
/// </summary>
public interface IKeyboardShortcutTargetRegistry
{
    IDisposable Register(IKeyboardShortcutTarget target);

    IKeyboardShortcutTarget? Current { get; }
}
