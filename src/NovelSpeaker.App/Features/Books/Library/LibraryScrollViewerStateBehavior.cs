using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace NovelSpeaker.App.Features.Books.Library;

/// <summary>
/// Adapts the logical Library scroll anchor to the standard ListBox lifecycle.
/// WPF owns row realization and scrolling; this adapter only captures visible card
/// identities and requests standard item bring-into-view behavior during restore.
/// </summary>
public static class LibraryScrollViewerStateBehavior
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(LibraryScrollState),
            typeof(LibraryScrollViewerStateBehavior),
            new PropertyMetadata(null, OnAttachedPropertyChanged));

    public static readonly DependencyProperty RowIndexByBookIdProperty =
        DependencyProperty.RegisterAttached(
            "RowIndexByBookId",
            typeof(IReadOnlyDictionary<string, LibraryBookRowPosition>),
            typeof(LibraryScrollViewerStateBehavior),
            new PropertyMetadata(null, OnAttachedPropertyChanged));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(Controller),
            typeof(LibraryScrollViewerStateBehavior),
            new PropertyMetadata(null));

    public static void SetState(DependencyObject element, LibraryScrollState? value) =>
        element.SetValue(StateProperty, value);

    public static LibraryScrollState? GetState(DependencyObject element) =>
        (LibraryScrollState?)element.GetValue(StateProperty);

    public static void SetRowIndexByBookId(
        DependencyObject element,
        IReadOnlyDictionary<string, LibraryBookRowPosition>? value) =>
        element.SetValue(RowIndexByBookIdProperty, value);

    public static IReadOnlyDictionary<string, LibraryBookRowPosition>? GetRowIndexByBookId(
        DependencyObject element) =>
        (IReadOnlyDictionary<string, LibraryBookRowPosition>?)element.GetValue(RowIndexByBookIdProperty);

    private static void OnAttachedPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ListBox listBox)
        {
            return;
        }

        var controller = (Controller?)listBox.GetValue(ControllerProperty);
        if (controller is null)
        {
            controller = new Controller(listBox);
            listBox.SetValue(ControllerProperty, controller);
        }

        controller.Refresh();
    }

    private sealed class Controller
    {
        private readonly ListBox _listBox;
        private INotifyCollectionChanged? _rows;
        private ScrollViewer? _scrollViewer;
        private bool _isRestoring;
        private int _restoreGeneration;
        private EventHandler? _pendingGeneratorStatusChanged;
        private EventHandler? _pendingLayoutUpdated;
        private int? _pendingRealizationGeneration;
        private ListBoxItem? _pendingRowContainer;
        private RoutedEventHandler? _pendingRowLoaded;

        public Controller(ListBox listBox)
        {
            _listBox = listBox;
            _listBox.Loaded += OnLoaded;
            _listBox.Unloaded += OnUnloaded;
            _listBox.SizeChanged += OnSizeChanged;
        }

        public void Refresh()
        {
            HookRows();
            if (_listBox.IsLoaded)
            {
                RestoreAnchor();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _scrollViewer = FindDescendant<ScrollViewer>(_listBox);
            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;
            }

            HookRows();
            RestoreAnchor();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CaptureCurrentAnchor();
            UnhookRows();
            CancelPendingRestoreReadiness();
            _isRestoring = false;
            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged -= OnScrollChanged;
                _scrollViewer = null;
            }

            _restoreGeneration++;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, _listBox) && !_isRestoring)
            {
                CaptureCurrentAnchor();
            }
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!_isRestoring && Math.Abs(e.VerticalChange) > 0d)
            {
                CaptureCurrentAnchor();
            }
        }

        private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RestoreAnchor();
        }

        private void HookRows()
        {
            if (_listBox.ItemsSource is not INotifyCollectionChanged rows || ReferenceEquals(rows, _rows))
            {
                return;
            }

            UnhookRows();
            _rows = rows;
            _rows.CollectionChanged += OnRowsCollectionChanged;
        }

        private void UnhookRows()
        {
            if (_rows is null)
            {
                return;
            }

            _rows.CollectionChanged -= OnRowsCollectionChanged;
            _rows = null;
        }

        private void RestoreAnchor()
        {
            if (!_listBox.IsLoaded)
            {
                return;
            }

            var generation = ++_restoreGeneration;
            CancelPendingRestoreReadiness();
            _isRestoring = false;
            if (GetState(_listBox) is not { AnchorBookId: { Length: > 0 } anchorBookId } state ||
                _listBox.Items.Count == 0)
            {
                return;
            }

            var rowPositions = GetRowIndexByBookId(_listBox);
            if (rowPositions is null)
            {
                return;
            }

            if (!rowPositions.TryGetValue(anchorBookId, out var position))
            {
                _scrollViewer?.ScrollToTop();
                state.Clear();

                return;
            }

            var rowIndex = position.RowIndex;
            if ((uint)rowIndex >= (uint)_listBox.Items.Count ||
                _listBox.Items[rowIndex] is not LibraryBookRowProjection rowProjection ||
                !rowProjection.Cards.Any(card =>
                    string.Equals(card.Book.BookId, anchorBookId, StringComparison.Ordinal)))
            {
                // The map can arrive just before the ItemsSource replacement. Keep
                // the anchor and let the committed row collection trigger restore.

                return;
            }

            var row = (object)rowProjection;
            _isRestoring = true;
            try
            {
                _listBox.ScrollIntoView(row);
            }
            catch
            {
                _isRestoring = false;
                throw;
            }

            if (_scrollViewer is not null)
            {
                var offset = state.RelativeOffset;
                QueueRelativeOffsetAfterRealization(generation, row, anchorBookId, offset);
            }
            else
            {
                _isRestoring = false;
            }
        }

        private void QueueRelativeOffsetAfterRealization(
            int generation,
            object row,
            string anchorBookId,
            double relativeOffset)
        {
            if (generation != _restoreGeneration || _scrollViewer is null)
            {
                return;
            }

            if (FindRowContainer(row) is { } rowContainer)
            {
                QueueRelativeOffsetAfterContainerLoaded(
                    generation,
                    row,
                    anchorBookId,
                    relativeOffset,
                    rowContainer);
                return;
            }

            _pendingRealizationGeneration = generation;
            var generator = _listBox.ItemContainerGenerator;
            EventHandler statusChanged = null!;
            statusChanged = (_, _) =>
            {
                if (generation != _restoreGeneration)
                {
                    DetachPendingGeneratorStatusChanged();
                    return;
                }

                if (generator.Status != GeneratorStatus.ContainersGenerated)
                {
                    return;
                }

                DetachPendingGeneratorStatusChanged();
                _pendingRealizationGeneration = null;
                QueueRelativeOffsetAfterRealization(
                    generation,
                    row,
                    anchorBookId,
                    relativeOffset);
            };
            _pendingGeneratorStatusChanged = statusChanged;
            generator.StatusChanged += statusChanged;

            // ScrollIntoView can realize synchronously. The generator event covers
            // deferred realization; this single callback covers the already-ready
            // path after WPF has completed the current layout pass.
            _scrollViewer.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () =>
                {
                    if (generation != _restoreGeneration ||
                        _pendingRealizationGeneration != generation)
                    {
                        return;
                    }

                    if (FindRowContainer(row) is not { } realizedRowContainer)
                    {
                        _pendingRealizationGeneration = null;
                        DetachPendingGeneratorStatusChanged();
                        _isRestoring = false;
                        QueueRelativeOffsetAfterNextLayout(
                            generation,
                            row,
                            anchorBookId,
                            relativeOffset);
                        return;
                    }

                    _pendingRealizationGeneration = null;
                    DetachPendingGeneratorStatusChanged();
                    QueueRelativeOffsetAfterContainerLoaded(
                        generation,
                        row,
                        anchorBookId,
                        relativeOffset,
                        realizedRowContainer);
                });
        }

        private void QueueRelativeOffsetAfterNextLayout(
            int generation,
            object row,
            string anchorBookId,
            double relativeOffset)
        {
            if (_pendingLayoutUpdated is not null)
            {
                return;
            }

            EventHandler layoutUpdated = null!;
            layoutUpdated = (_, _) =>
            {
                _listBox.LayoutUpdated -= layoutUpdated;
                if (ReferenceEquals(_pendingLayoutUpdated, layoutUpdated))
                {
                    _pendingLayoutUpdated = null;
                }

                if (generation != _restoreGeneration)
                {
                    return;
                }

                if (FindRowContainer(row) is { } realizedRowContainer)
                {
                    QueueRelativeOffsetAfterContainerLoaded(
                        generation,
                        row,
                        anchorBookId,
                        relativeOffset,
                        realizedRowContainer);
                }
            };

            _pendingLayoutUpdated = layoutUpdated;
            _listBox.LayoutUpdated += layoutUpdated;
        }

        private void QueueRelativeOffsetAfterContainerLoaded(
            int generation,
            object row,
            string anchorBookId,
            double relativeOffset,
            ListBoxItem rowContainer)
        {
            if (generation != _restoreGeneration || _scrollViewer is null)
            {
                return;
            }

            if (rowContainer.IsLoaded)
            {
                _scrollViewer.Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    () => ApplyRelativeOffset(generation, row, anchorBookId, relativeOffset));
                return;
            }

            RoutedEventHandler rowLoaded = null!;
            rowLoaded = (_, _) =>
            {
                rowContainer.Loaded -= rowLoaded;
                if (ReferenceEquals(_pendingRowLoaded, rowLoaded))
                {
                    _pendingRowContainer = null;
                    _pendingRowLoaded = null;
                }

                if (generation == _restoreGeneration)
                {
                    _scrollViewer?.Dispatcher.BeginInvoke(
                        DispatcherPriority.Loaded,
                        () => ApplyRelativeOffset(generation, row, anchorBookId, relativeOffset));
                }
            };
            _pendingRowContainer = rowContainer;
            _pendingRowLoaded = rowLoaded;
            rowContainer.Loaded += rowLoaded;
        }

        private void CancelPendingRestoreReadiness()
        {
            _pendingRealizationGeneration = null;
            DetachPendingGeneratorStatusChanged();
            if (_pendingLayoutUpdated is not null)
            {
                _listBox.LayoutUpdated -= _pendingLayoutUpdated;
            }

            _pendingLayoutUpdated = null;
            if (_pendingRowContainer is not null && _pendingRowLoaded is not null)
            {
                _pendingRowContainer.Loaded -= _pendingRowLoaded;
            }

            _pendingRowContainer = null;
            _pendingRowLoaded = null;
        }

        private void DetachPendingGeneratorStatusChanged()
        {
            if (_pendingGeneratorStatusChanged is null)
            {
                return;
            }

            _listBox.ItemContainerGenerator.StatusChanged -= _pendingGeneratorStatusChanged;
            _pendingGeneratorStatusChanged = null;
        }

        private void ApplyRelativeOffset(
            int generation,
            object row,
            string anchorBookId,
            double relativeOffset)
        {
            if (generation != _restoreGeneration ||
                !_listBox.IsLoaded ||
                GetState(_listBox) is not { AnchorBookId: { } currentAnchor } state ||
                !string.Equals(currentAnchor, anchorBookId, StringComparison.Ordinal) ||
                _scrollViewer is null ||
                FindRowContainer(row) is not { } rowContainer)
            {
                if (generation == _restoreGeneration)
                {
                    _isRestoring = false;
                }

                return;
            }

            try
            {
                var rowTop = rowContainer.TransformToAncestor(_scrollViewer).Transform(new Point()).Y;
                var adjustment = Math.Clamp(
                    rowTop - relativeOffset,
                    -Math.Max(1d, _scrollViewer.ViewportHeight),
                    Math.Max(1d, _scrollViewer.ViewportHeight));
                if (Math.Abs(adjustment) < 0.5d)
                {
                    _isRestoring = false;
                    return;
                }

                _isRestoring = true;
                try
                {
                    _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset + adjustment);
                }
                finally
                {
                    _isRestoring = false;
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                if (generation == _restoreGeneration)
                {
                    _isRestoring = false;
                }
            }
        }

        private ListBoxItem? FindRowContainer(object row)
        {
            return _listBox.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem;
        }

        private void CaptureCurrentAnchor()
        {
            var state = GetState(_listBox);
            if (state is null || _scrollViewer is null)
            {
                return;
            }

            var positions = new List<LibraryVisibleBookPosition>();
            foreach (var card in FindDescendants<BookCardView>(_listBox))
            {
                if (card.Item is not { } book || card.ActualHeight <= 0d)
                {
                    continue;
                }

                try
                {
                    var point = card.TransformToAncestor(_scrollViewer).Transform(new Point());
                    positions.Add(new LibraryVisibleBookPosition(
                        book.BookId,
                        point.Y,
                        point.Y + card.ActualHeight));
                }
                catch (InvalidOperationException)
                {
                }
            }

            if (positions.Count > 0)
            {
                state.Capture(positions);
            }
        }

        private static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null)
            where T : DependencyObject
        {
            foreach (var descendant in FindDescendants(root, predicate))
            {
                return descendant;
            }

            return null;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root, Func<T, bool>? predicate = null)
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
}
