using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NovelSpeaker.App.Shared.Presentation.Rules;

/// <summary>Owns a single insertion slot and feedback line for one list, including its gaps.</summary>
public static class RuleListDragBehavior
{
    public static readonly DependencyProperty ReorderCommandProperty = DependencyProperty.RegisterAttached(
        "ReorderCommand", typeof(ICommand), typeof(RuleListDragBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    private static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached(
        "Feedback", typeof(SlotAdorner), typeof(RuleListDragBehavior));

    public static ICommand? GetReorderCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ReorderCommandProperty);

    public static void SetReorderCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(ReorderCommandProperty, value);

    private static void OnCommandChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ItemsControl list)
        {
            return;
        }

        if (args.OldValue is null && args.NewValue is not null)
        {
            list.AllowDrop = true;
            list.PreviewDragOver += OnDragOver;
            list.PreviewDragLeave += OnDragLeave;
            list.PreviewDrop += OnDrop;
            list.Unloaded += OnUnloaded;
        }
        else if (args.NewValue is null)
        {
            list.PreviewDragOver -= OnDragOver;
            list.PreviewDragLeave -= OnDragLeave;
            list.PreviewDrop -= OnDrop;
            list.Unloaded -= OnUnloaded;
            Clear(list);
        }
    }

    private static void OnDragOver(object sender, DragEventArgs args)
    {
        var list = (ItemsControl)sender;
        Clear(list);
        if (!TryResolve(list, args, out var request, out var lineY) ||
            GetReorderCommand(list)?.CanExecute(request) != true)
        {
            args.Effects = DragDropEffects.None;
        }
        else
        {
            if (AdornerLayer.GetAdornerLayer(list) is { } layer)
            {
                var feedback = new SlotAdorner(list, lineY);
                list.SetValue(FeedbackProperty, feedback);
                layer.Add(feedback);
            }

            args.Effects = DragDropEffects.Move;
        }

        args.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs args)
    {
        var list = (ItemsControl)sender;
        Clear(list);
        args.Effects = DragDropEffects.None;
        if (TryResolve(list, args, out var request, out _) &&
            GetReorderCommand(list) is { } command && command.CanExecute(request))
        {
            command.Execute(request);
            args.Effects = DragDropEffects.Move;
        }

        args.Handled = true;
    }

    private static bool TryResolve(ItemsControl list, DragEventArgs args, out RuleReorderRequest request, out double lineY)
    {
        request = null!;
        lineY = 0;
        if (args.Data.GetData(typeof(ListDragItemView.RuleDragPayload)) is not ListDragItemView.RuleDragPayload payload ||
            !ReferenceEquals(payload.Owner, list))
        {
            return false;
        }

        var views = FindItems(list).Where(view => view.IsVisible && view.IsSortable)
            .Select(view => (View: view, Container: ItemsControl.ContainerFromElement(list, view)))
            .Where(item => item.Container is not null)
            .Select(item => (Index: list.ItemContainerGenerator.IndexFromContainer(item.Container),
                Bounds: item.View.TransformToAncestor(list).TransformBounds(new Rect(item.View.RenderSize))))
            .Where(item => item.Index >= 0)
            .OrderBy(item => item.Index).ToArray();
        if (views.Length == 0)
        {
            return false;
        }

        var pointerY = args.GetPosition(list).Y;
        for (var index = 0; index < views.Length; index++)
        {
            var item = views[index];
            if (pointerY < item.Bounds.Top + item.Bounds.Height / 2)
            {
                request = new RuleReorderRequest(payload.Source, item.Index);
                lineY = item.Index == 0 ? Math.Max(1, item.Bounds.Top) : item.Bounds.Top;

                return true;
            }
        }

        var last = views[^1];
        request = new RuleReorderRequest(payload.Source, last.Index + 1);
        lineY = Math.Min(list.ActualHeight - 1, last.Bounds.Bottom);
        return true;
    }

    private static IEnumerable<ListDragItemView> FindItems(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ListDragItemView view)
            {
                yield return view;
            }
            else
            {
                foreach (var item in FindItems(child))
                {
                    yield return item;
                }
            }
        }
    }

    private static void OnDragLeave(object sender, DragEventArgs args) => Clear((ItemsControl)sender);

    private static void OnUnloaded(object sender, RoutedEventArgs args) => Clear((ItemsControl)sender);

    internal static void Clear(ItemsControl? list)
    {
        if (list?.GetValue(FeedbackProperty) is SlotAdorner feedback)
        {
            AdornerLayer.GetAdornerLayer(list)?.Remove(feedback);
            list.ClearValue(FeedbackProperty);
        }
    }

    private sealed class SlotAdorner : Adorner
    {
        private readonly double _lineY;

        public SlotAdorner(ItemsControl list, double lineY) : base(list)
        {
            _lineY = lineY;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            drawingContext.PushClip(new RectangleGeometry(new Rect(AdornedElement.RenderSize)));
            var brush = (Brush)((FrameworkElement)AdornedElement).FindResource("App.Brush.Accent.Default");
            drawingContext.DrawLine(new Pen(brush, 2), new Point(8, _lineY),
                new Point(Math.Max(8, AdornedElement.RenderSize.Width - 8), _lineY));
            drawingContext.Pop();
        }
    }
}
