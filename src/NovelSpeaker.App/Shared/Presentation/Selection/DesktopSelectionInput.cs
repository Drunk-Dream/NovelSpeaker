using System.Windows.Input;

namespace NovelSpeaker.App.Shared.Presentation.Selection;

/// <summary>Translates the WPF keyboard state at the page's selection entry point.</summary>
internal static class DesktopSelectionInput
{
    public static DesktopSelectionModifiers ReadModifiers()
    {
        var keys = Keyboard.Modifiers;
        var modifiers = DesktopSelectionModifiers.None;
        if (keys.HasFlag(ModifierKeys.Control)) modifiers |= DesktopSelectionModifiers.Control;
        if (keys.HasFlag(ModifierKeys.Shift)) modifiers |= DesktopSelectionModifiers.Shift;
        return modifiers;
    }
}
