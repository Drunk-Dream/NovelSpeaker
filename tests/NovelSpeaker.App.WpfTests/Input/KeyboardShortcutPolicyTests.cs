using System.Windows.Input;
using NovelSpeaker.App.Shell.Input;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Input;

public sealed class KeyboardShortcutPolicyTests
{
    [Fact]
    public void Resolve_maps_shell_shortcuts()
    {
        foreach (var (key, modifiers, expected) in new[]
                 {
                     (Key.O, ModifierKeys.Control, KeyboardShortcutAction.ImportTextFile),
                     (Key.OemComma, ModifierKeys.Control, KeyboardShortcutAction.OpenSettings),
                     (Key.Escape, ModifierKeys.None, KeyboardShortcutAction.NavigateBack),
                     (Key.Left, ModifierKeys.Alt, KeyboardShortcutAction.NavigateBack)
                 })
        {
            var action = KeyboardShortcutPolicy.Resolve(key, modifiers, new KeyboardShortcutContext(false, false, false));
            Assert.Equal(expected, action);
        }
    }

    [Fact]
    public void Resolve_maps_playback_shortcuts_only_on_player_page()
    {
        foreach (var (key, modifiers, expected) in new[]
                 {
                     (Key.Space, ModifierKeys.None, KeyboardShortcutAction.TogglePlayback),
                     (Key.Left, ModifierKeys.Control, KeyboardShortcutAction.PreviousSegment),
                     (Key.Right, ModifierKeys.Control, KeyboardShortcutAction.NextSegment),
                     (Key.Left, ModifierKeys.Control | ModifierKeys.Shift, KeyboardShortcutAction.PreviousChapter),
                     (Key.Right, ModifierKeys.Control | ModifierKeys.Shift, KeyboardShortcutAction.NextChapter)
                 })
        {
            Assert.Equal(expected, KeyboardShortcutPolicy.Resolve(key, modifiers, new KeyboardShortcutContext(true, false, false)));
            Assert.Null(KeyboardShortcutPolicy.Resolve(key, modifiers, new KeyboardShortcutContext(false, false, false)));
        }
    }

    [Fact]
    public void Resolve_suppresses_shortcuts_while_editing_or_transient_ui_is_open()
    {
        foreach (var (key, modifiers, isTextEditing, isTransientUiOpen) in new[]
                 {
                     (Key.O, ModifierKeys.Control, true, false),
                     (Key.OemComma, ModifierKeys.Control, false, true),
                     (Key.Escape, ModifierKeys.None, true, false),
                     (Key.Left, ModifierKeys.Alt, false, true),
                     (Key.Space, ModifierKeys.None, true, false)
                 })
        {
            var action = KeyboardShortcutPolicy.Resolve(
                key,
                modifiers,
                new KeyboardShortcutContext(true, isTextEditing, isTransientUiOpen));

            Assert.Null(action);
        }
    }

    [Fact]
    public void Resolve_keeps_alt_left_as_an_application_back_action_while_editing()
    {
        var action = KeyboardShortcutPolicy.Resolve(
            Key.Left,
            ModifierKeys.Alt,
            new KeyboardShortcutContext(true, true, false));

        Assert.Equal(KeyboardShortcutAction.NavigateBack, action);
    }
}
