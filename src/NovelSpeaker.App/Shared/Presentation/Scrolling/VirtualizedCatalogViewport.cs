using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NovelSpeaker.App.Shared.Presentation.Scrolling;

/// <summary>
/// Maps the realized portion of a pixel-scrolled virtualized catalog to a bounded decoration
/// window. The scan is limited to a small estimate around the current offset and never walks the
/// full catalog.
/// </summary>
internal static class VirtualizedCatalogViewport
{
    public static (int Start, int Count) GetWindow(
        ListBox listBox,
        ScrollViewer scrollViewer,
        int itemCount,
        int windowSize)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(scrollViewer);
        if (itemCount <= 0 || windowSize <= 0)
        {
            return (0, 0);
        }

        var estimatedStart = EstimateStart(scrollViewer, itemCount);
        var searchStart = Math.Max(0, estimatedStart - windowSize);
        var searchEnd = Math.Min(itemCount, estimatedStart + (windowSize * 2));
        var firstRealized = -1;
        var lastRealized = -1;
        for (var index = searchStart; index < searchEnd; index++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem)
            {
                continue;
            }

            firstRealized = firstRealized < 0 ? index : firstRealized;
            lastRealized = index;
        }

        var start = firstRealized >= 0 ? firstRealized : estimatedStart;
        if (lastRealized >= start)
        {
            var realizedCount = lastRealized - start + 1;
            start = Math.Max(0, start - Math.Max(0, (windowSize - realizedCount) / 2));
        }

        start = Math.Clamp(start, 0, Math.Max(0, itemCount - 1));
        return (start, Math.Min(windowSize, itemCount - start));
    }

    private static int EstimateStart(ScrollViewer scrollViewer, int itemCount)
    {
        var scrollableHeight = scrollViewer.ExtentHeight - scrollViewer.ViewportHeight;
        if (scrollableHeight <= 0)
        {
            return 0;
        }

        var ratio = Math.Clamp(scrollViewer.VerticalOffset / scrollableHeight, 0, 1);
        return (int)Math.Round(ratio * Math.Max(0, itemCount - 1));
    }
}
