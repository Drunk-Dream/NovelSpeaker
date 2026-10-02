using System.Windows;
using NovelSpeaker.App.Shared.Presentation;

namespace NovelSpeaker.App.Shell.Input;

public interface IShortcutContextResolver
{
    KeyboardShortcutContext Resolve(
        bool isPlayerPageActive,
        DependencyObject? focusedElement,
        DependencyObject dialogHost,
        ITransientEscapeHandler? activePageEscapeHandler);
}
