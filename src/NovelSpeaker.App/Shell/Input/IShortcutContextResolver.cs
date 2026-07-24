using System.Windows;

namespace NovelSpeaker.App.Shell.Input;

public interface IShortcutContextResolver
{
    KeyboardShortcutContext Resolve(
        bool isPlayerPageActive,
        DependencyObject? focusedElement,
        DependencyObject dialogHost);
}
