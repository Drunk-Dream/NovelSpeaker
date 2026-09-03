using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NovelSpeaker.App.Shared.Presentation;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shell.Input;

public sealed class WpfShortcutContextResolver : IShortcutContextResolver
{
    public KeyboardShortcutContext Resolve(
        bool isPlayerPageActive,
        DependencyObject? focusedElement,
        DependencyObject dialogHost)
    {
        ArgumentNullException.ThrowIfNull(dialogHost);

        return new KeyboardShortcutContext(
            isPlayerPageActive,
            HasEditingAncestor(focusedElement),
            HasTransientUiAncestor(focusedElement) ||
            IsHostedInPopupSurface(focusedElement) ||
            FindVisibleContentDialog(dialogHost) is not null,
            FindTransientEscapeHandler(focusedElement));
    }

    private static bool HasEditingAncestor(DependencyObject? element)
    {
        for (var current = element; current is not null; current = GetParent(current))
        {
            if (current is TextBoxBase or System.Windows.Controls.PasswordBox ||
                current is ComboBox { IsEditable: true })
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTransientUiAncestor(DependencyObject? element)
    {
        for (var current = element; current is not null; current = GetParent(current))
        {
            if (current is Popup { IsOpen: true } ||
                current is ComboBox { IsDropDownOpen: true } ||
                current is ContextMenu { IsOpen: true } ||
                current is System.Windows.Controls.MenuItem)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHostedInPopupSurface(DependencyObject? element)
    {
        var presentationSource = element is Visual or Visual3D
            ? PresentationSource.FromDependencyObject(element)
            : null;

        return presentationSource?.RootVisual is not null and not Window;
    }

    private static ITransientEscapeHandler? FindTransientEscapeHandler(DependencyObject? element)
    {
        for (var current = element; current is not null; current = GetParent(current))
        {
            if (current is FrameworkElement { DataContext: ITransientEscapeHandler handler })
            {
                return handler;
            }

            if (current is FrameworkContentElement { DataContext: ITransientEscapeHandler contentHandler })
            {
                return contentHandler;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        return LogicalTreeHelper.GetParent(current) ??
               (current is Visual or Visual3D
                   ? VisualTreeHelper.GetParent(current)
                   : null);
    }

    private static ContentDialog? FindVisibleContentDialog(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ContentDialog { IsVisible: true } dialog)
            {
                return dialog;
            }

            var nested = FindVisibleContentDialog(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
