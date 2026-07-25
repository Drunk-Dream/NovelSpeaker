using System.Windows;
using System.Windows.Media;

namespace NovelSpeaker.UnitTests;

internal static class VisualTreeTestHelper
{
    public static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        return FindDescendants<T>(root).FirstOrDefault();
    }

    public static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        return FindDescendants(root, predicate).FirstOrDefault();
    }

    public static IEnumerable<T> FindDescendants<T>(DependencyObject root, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T typedChild && (predicate is null || predicate(typedChild)))
            {
                yield return typedChild;
            }

            foreach (var descendant in FindDescendants(child, predicate))
            {
                yield return descendant;
            }
        }
    }
}
