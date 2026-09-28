using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovelSpeaker.App.Shared.Presentation.Rules;

/// <summary>Shares only drag gestures, list ownership, and edge scrolling across feature-owned cards.</summary>
public abstract class ListDragItemView : Control
{
    private const double AutoScrollEdgeSize = 32;
    private readonly RuleDragGestureStateMachine _dragGesture = new(
        minimumHorizontalDistance: SystemParameters.MinimumHorizontalDragDistance,
        minimumVerticalDistance: SystemParameters.MinimumVerticalDragDistance);

    protected ListDragItemView()
    {
        Focusable = false;
        IsTabStop = false;
    }

    public static readonly DependencyProperty IsSortableProperty = DependencyProperty.Register(
        nameof(IsSortable),
        typeof(bool),
        typeof(ListDragItemView),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsDraggingProperty = DependencyProperty.Register(
        nameof(IsDragging),
        typeof(bool),
        typeof(ListDragItemView),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter),
        typeof(object),
        typeof(ListDragItemView),
        new FrameworkPropertyMetadata(null));

    public bool IsSortable
    {
        get => (bool)GetValue(IsSortableProperty);
        set => SetValue(IsSortableProperty, value);
    }

    public bool IsDragging
    {
        get => (bool)GetValue(IsDraggingProperty);
        set => SetValue(IsDraggingProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    protected virtual bool IsDragExcluded(DependencyObject? source) => false;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!IsSortable)
        {
            return;
        }

        _dragGesture.Press(
            e.GetPosition(this),
            e.Timestamp,
            IsDragExcluded(e.OriginalSource as DependencyObject));
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!IsSortable ||
            !_dragGesture.ShouldBeginDrag(
                e.GetPosition(this),
                e.Timestamp,
                e.LeftButton == MouseButtonState.Pressed))
        {
            return;
        }

        IsDragging = true;
        try
        {
            var source = CommandParameter ?? DataContext;
            if (source is null)
            {
                return;
            }

            DragDrop.DoDragDrop(
                this,
                new RuleDragPayload(source, FindAncestor<ItemsControl>(this)),
                DragDropEffects.Move);
        }
        finally
        {
            IsDragging = false;
            RuleListDragBehavior.Clear(FindAncestor<ItemsControl>(this));
        }

        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragGesture.Cancel();
        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragGesture.Cancel();
        base.OnLostMouseCapture(e);
    }

    protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
    {
        if (IsDragging && FindAncestor<ScrollViewer>(this) is { } scrollViewer)
        {
            ScrollAtListEdge(scrollViewer, Mouse.GetPosition(scrollViewer).Y);
        }

        base.OnQueryContinueDrag(e);
    }

    internal static int ResolveEdgeScrollDirection(ScrollViewer scrollViewer, double pointerY) =>
        RuleDragGeometry.ResolveEdgeScrollDirection(
            pointerY,
            scrollViewer.RenderSize.Height,
            AutoScrollEdgeSize);

    private static DependencyObject? GetParent(DependencyObject current) =>
        current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);

    private static void ScrollAtListEdge(ScrollViewer scrollViewer, double pointerY)
    {
        var direction = ResolveEdgeScrollDirection(scrollViewer, pointerY);
        if (direction < 0)
        {
            scrollViewer.LineUp();
        }
        else if (direction > 0)
        {
            scrollViewer.LineDown();
        }
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var current = GetParent(start); current is not null; current = GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    internal sealed record RuleDragPayload(object Source, ItemsControl? Owner);
}
