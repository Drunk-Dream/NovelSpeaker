using System.Windows.Input;
using NovelSpeaker.App.Shell.Input;
using Xunit;

namespace NovelSpeaker.UnitTests.Input;

public sealed class KeyboardShortcutPolicyTests
{
    [Theory]
    [InlineData(Key.O, ModifierKeys.Control, KeyboardShortcutAction.ImportTextFile)]
    [InlineData(Key.OemComma, ModifierKeys.Control, KeyboardShortcutAction.OpenSettings)]
    [InlineData(Key.Escape, ModifierKeys.None, KeyboardShortcutAction.NavigateBack)]
    [InlineData(Key.Left, ModifierKeys.Alt, KeyboardShortcutAction.NavigateBack)]
    public void Resolve_maps_shell_shortcuts(Key key, ModifierKeys modifiers, KeyboardShortcutAction expected)
    {
        var action = KeyboardShortcutPolicy.Resolve(key, modifiers, new KeyboardShortcutContext(false, false, false));

        Assert.Equal(expected, action);
    }

    [Theory]
    [InlineData(Key.Space, ModifierKeys.None, KeyboardShortcutAction.TogglePlayback)]
    [InlineData(Key.Left, ModifierKeys.Control, KeyboardShortcutAction.PreviousSegment)]
    [InlineData(Key.Right, ModifierKeys.Control, KeyboardShortcutAction.NextSegment)]
    [InlineData(Key.Left, ModifierKeys.Control | ModifierKeys.Shift, KeyboardShortcutAction.PreviousChapter)]
    [InlineData(Key.Right, ModifierKeys.Control | ModifierKeys.Shift, KeyboardShortcutAction.NextChapter)]
    public void Resolve_maps_playback_shortcuts_only_on_player_page(Key key, ModifierKeys modifiers, KeyboardShortcutAction expected)
    {
        Assert.Equal(expected, KeyboardShortcutPolicy.Resolve(key, modifiers, new KeyboardShortcutContext(true, false, false)));
        Assert.Null(KeyboardShortcutPolicy.Resolve(key, modifiers, new KeyboardShortcutContext(false, false, false)));
    }

    [Theory]
    [InlineData(Key.O, ModifierKeys.Control, true, false)]
    [InlineData(Key.OemComma, ModifierKeys.Control, false, true)]
    [InlineData(Key.Escape, ModifierKeys.None, true, false)]
    [InlineData(Key.Left, ModifierKeys.Alt, false, true)]
    [InlineData(Key.Space, ModifierKeys.None, true, false)]
    public void Resolve_suppresses_all_shortcuts_while_editing_or_transient_ui_is_open(
        Key key,
        ModifierKeys modifiers,
        bool isTextEditing,
        bool isTransientUiOpen)
    {
        var action = KeyboardShortcutPolicy.Resolve(
            key,
            modifiers,
            new KeyboardShortcutContext(true, isTextEditing, isTransientUiOpen));

        Assert.Null(action);
    }
}
