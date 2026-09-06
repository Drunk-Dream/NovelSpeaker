using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NovelSpeaker.App.Features.Books.Library;

public static class LibraryScrollViewerStateBehavior
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(LibraryScrollState),
            typeof(LibraryScrollViewerStateBehavior),
            new PropertyMetadata(null, OnAttachedPropertyChanged));

    public static readonly DependencyProperty ItemsControlProperty =
        DependencyProperty.RegisterAttached(
            "ItemsControl",
            typeof(ItemsControl),
            typeof(LibraryScrollViewerStateBehavior),
            new PropertyMetadata(null, OnAttachedPropertyChanged));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(Controller),
            typeof(LibraryScrollViewerStateBehavior),
            new PropertyMetadata(null));

    public static void SetState(DependencyObject element, LibraryScrollState? value)
    {
        element.SetValue(StateProperty, value);
    }

    public static LibraryScrollState? GetState(DependencyObject element)
    {
        return (LibraryScrollState?)element.GetValue(StateProperty);
    }

    public static void SetItemsControl(DependencyObject element, ItemsControl? value)
    {
        element.SetValue(ItemsControlProperty, value);
    }

    public static ItemsControl? GetItemsControl(DependencyObject element)
    {
        return (ItemsControl?)element.GetValue(ItemsControlProperty);
    }

    private static void OnAttachedPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ScrollViewer scrollViewer)
        {
            return;
        }

        var controller = (Controller?)scrollViewer.GetValue(ControllerProperty);
        if (controller is null)
        {
            controller = new Controller(scrollViewer);
            scrollViewer.SetValue(ControllerProperty, controller);
        }

        controller.Refresh();
    }

    private sealed class Controller
    {
        private readonly ScrollViewer _scrollViewer;
        private INotifyCollectionChanged? _itemsSource;
        private int _restoreAttemptsRemaining;

        public Controller(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            _scrollViewer.Loaded += OnLoaded;
            _scrollViewer.Unloaded += OnUnloaded;
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        public void Refresh()
        {
            HookCollectionChanged();
            ScheduleRestore();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HookCollectionChanged();
            ScheduleRestore();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CaptureCurrentAnchor();
            UnhookCollectionChanged();
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) > 0)
            {
                CaptureCurrentAnchor();
            }
        }

        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScheduleRestore();
        }

        private void HookCollectionChanged()
        {
            var itemsControl = GetItemsControl(_scrollViewer);
            if (itemsControl?.ItemsSource is not INotifyCollectionChanged source ||
                ReferenceEquals(source, _itemsSource))
            {
                return;
            }

            UnhookCollectionChanged();
            _itemsSource = source;
            _itemsSource.CollectionChanged += OnItemsCollectionChanged;
        }

        private void UnhookCollectionChanged()
        {
            if (_itemsSource is null)
            {
                return;
            }

            _itemsSource.CollectionChanged -= OnItemsCollectionChanged;
            _itemsSource = null;
        }

        private void ScheduleRestore()
        {
            _restoreAttemptsRemaining = 12;
            _scrollViewer.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                TryRestoreAnchor);
        }

        private void TryRestoreAnchor()
        {
            if (_restoreAttemptsRemaining <= 0)
            {
                return;
            }

            var state = GetState(_scrollViewer);
            var itemsControl = GetItemsControl(_scrollViewer);
            if (state is null || itemsControl is null)
            {
                return;
            }

            if (TryGetVirtualizedRestoreOffset(itemsControl, state, out var virtualizedOffset))
            {
                _scrollViewer.ScrollToVerticalOffset(virtualizedOffset);
                _restoreAttemptsRemaining = 0;
                return;
            }

            var positions = GetItemPositions(itemsControl);
            if (positions.Count == 0)
            {
                _restoreAttemptsRemaining--;
                if (_restoreAttemptsRemaining > 0)
                {
                    _scrollViewer.Dispatcher.BeginInvoke(
                        DispatcherPriority.Loaded,
                        TryRestoreAnchor);
                }

                return;
            }

            if (state.TryGetRestoreOffset(positions, out var offset))
            {
                _scrollViewer.ScrollToVerticalOffset(offset);
            }
            else
            {
                _scrollViewer.ScrollToTop();
            }

            _restoreAttemptsRemaining = 0;
        }

        private void CaptureCurrentAnchor()
        {
            var state = GetState(_scrollViewer);
            var itemsControl = GetItemsControl(_scrollViewer);
            if (state is null || itemsControl is null)
            {
                return;
            }

            var positions = GetItemPositions(itemsControl);
            if (positions.Count > 0)
            {
                state.Capture(positions);
            }
        }

        private List<LibraryVisibleBookPosition> GetItemPositions(ItemsControl itemsControl)
        {
            var positions = new List<LibraryVisibleBookPosition>();

            if (FindDescendant<LibraryResponsivePanel>(itemsControl) is { } panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    if (itemsControl.ItemContainerGenerator.ItemFromContainer(child) is not LibraryBookCardProjection book ||
                        child is not FrameworkElement container ||
                        container.ActualHeight <= 0)
                    {
                        continue;
                    }

                    AddItemPosition(book, container, positions);
                }

                positions.Sort(static (left, right) => left.Top.CompareTo(right.Top));
                return positions;
            }

            foreach (var item in itemsControl.Items)
            {
                if (itemsControl.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container ||
                    item is not LibraryBookCardProjection book ||
                    container.ActualHeight <= 0)
                {
                    continue;
                }

                AddItemPosition(book, container, positions);
            }

            positions.Sort(static (left, right) => left.Top.CompareTo(right.Top));
            return positions;
        }

        private bool TryGetVirtualizedRestoreOffset(
            ItemsControl itemsControl,
            LibraryScrollState state,
            out double verticalOffset)
        {
            verticalOffset = 0d;
            if (string.IsNullOrWhiteSpace(state.AnchorBookId) ||
                FindDescendant<LibraryResponsivePanel>(itemsControl) is not { } panel ||
                itemsControl is not LibraryItemsControl libraryItemsControl ||
                !libraryItemsControl.TryGetItemIndex(state.AnchorBookId, out var itemIndex))
            {
                return false;
            }

            return panel.TryGetVerticalOffset(itemIndex, state.RelativeOffset, out verticalOffset);
        }

        private void AddItemPosition(
            LibraryBookCardProjection book,
            FrameworkElement container,
            List<LibraryVisibleBookPosition> positions)
        {
            try
            {
                var point = container.TransformToAncestor(_scrollViewer).Transform(new Point(0, 0));
                positions.Add(new LibraryVisibleBookPosition(book.BookId, point.Y, point.Y + container.ActualHeight));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static T? FindDescendant<T>(DependencyObject root)
            where T : DependencyObject
        {
            for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
            {
                var child = VisualTreeHelper.GetChild(root, childIndex);
                if (child is T match)
                {
                    return match;
                }

                if (FindDescendant<T>(child) is { } descendant)
                {
                    return descendant;
                }
            }

            return null;
        }
    }
}
