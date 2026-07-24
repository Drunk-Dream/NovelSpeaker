namespace NovelSpeaker.App.Shell.Input;

/// <summary>
/// Describes UI state that decides whether an application-level shortcut may run.
/// </summary>
public sealed record KeyboardShortcutContext(
    bool IsPlayerPageActive,
    bool IsTextEditing,
    bool IsTransientUiOpen);
