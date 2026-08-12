using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace NovelSpeaker.TestKit.Wpf;

internal static class TransientPopupVisualRenderer
{
    public static bool HasOpenPopups(DependencyObject root)
    {
        var layers = new Dictionary<FrameworkElement, Flyout?>();
        Visit(root, layers);
        return layers.Count > 0;
    }

    public static IReadOnlyList<TransientPopupLayer> CaptureOpenLayers(
        FrameworkElement root,
        double dpi)
    {
        var layerRoots = new Dictionary<FrameworkElement, Flyout?>();
        Visit(root, layerRoots);
        return layerRoots
            .Select(layerRoot => CaptureLayer(root, layerRoot.Key, layerRoot.Value, dpi))
            .Where(layer => layer is not null)
            .Cast<TransientPopupLayer>()
            .ToArray();
    }

    public static BitmapSource Composite(
        BitmapSource background,
        Size size,
        double dpi,
        IReadOnlyList<TransientPopupLayer> layers)
    {
        if (layers.Count == 0)
        {
            return background;
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(background, new Rect(new Point(), size));
            foreach (var layer in layers)
            {
                context.DrawImage(layer.Bitmap, new Rect(layer.Origin, layer.Size));
            }
        }

        var composite = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(size.Width * dpi / 96d)),
            Math.Max(1, (int)Math.Ceiling(size.Height * dpi / 96d)),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        composite.Render(visual);
        composite.Freeze();
        return composite;
    }

    private static void Visit(
        DependencyObject current,
        IDictionary<FrameworkElement, Flyout?> layerRoots)
    {
        if (current is Popup { IsOpen: true, Child: FrameworkElement popupChild } popup &&
            popup.TemplatedParent is not Flyout)
        {
            layerRoots.TryAdd(popupChild, null);
        }

        if (current is Flyout { IsOpen: true, Content: FrameworkElement flyoutContent } flyout)
        {
            flyout.ApplyTemplate();
            layerRoots[flyoutContent] = flyout;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
        {
            Visit(VisualTreeHelper.GetChild(current, index), layerRoots);
        }
    }

    private static TransientPopupLayer? CaptureLayer(
        FrameworkElement root,
        FrameworkElement popupChild,
        Flyout? owner,
        double dpi)
    {
        popupChild.UpdateLayout();
        if (popupChild.ActualWidth <= 0 || popupChild.ActualHeight <= 0)
        {
            return null;
        }

        var size = new Size(popupChild.ActualWidth, popupChild.ActualHeight);
        var origin = owner is null
            ? GetScreenRelativeOrigin(root, popupChild)
            : GetFlyoutOrigin(root, owner, size);
        if (origin is null)
        {
            return null;
        }

        var localDataContext = popupChild.ReadLocalValue(FrameworkElement.DataContextProperty);
        var inheritedDataContext = popupChild.DataContext;
        var itemControls = FindDescendants<ItemsControl>(popupChild)
            .Select(itemsControl => new ItemsControlCaptureState(
                itemsControl,
                itemsControl.ReadLocalValue(VirtualizingPanel.IsVirtualizingProperty),
                itemsControl.ReadLocalValue(ScrollViewer.CanContentScrollProperty)))
            .ToArray();
        WpfWindowHost? captureHost = null;
        Border? captureRoot = null;
        try
        {
            if (owner is not null)
            {
                foreach (var state in itemControls)
                {
                    VirtualizingPanel.SetIsVirtualizing(state.ItemsControl, false);
                    ScrollViewer.SetCanContentScroll(state.ItemsControl, false);
                }

                owner.Content = null;
                if (localDataContext == DependencyProperty.UnsetValue)
                {
                    popupChild.DataContext = inheritedDataContext;
                }

                captureRoot = new Border
                {
                    Width = size.Width,
                    Height = size.Height,
                    Child = popupChild
                };
                var captureWindow = new Window
                {
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Content = captureRoot,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize
                };
                captureHost = WpfWindowHost.Show(captureWindow);
                captureWindow.UpdateLayout();
            }

            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(size.Width * dpi / 96d)),
                Math.Max(1, (int)Math.Ceiling(size.Height * dpi / 96d)),
                dpi,
                dpi,
                PixelFormats.Pbgra32);
            bitmap.Render(popupChild);
            bitmap.Freeze();
            EnsureVisiblePixels(bitmap);
            return new TransientPopupLayer(origin.Value, size, bitmap);
        }
        finally
        {
            captureHost?.Dispose();
            if (captureRoot is not null)
            {
                captureRoot.Child = null;
            }
            if (owner is not null)
            {
                owner.Content = popupChild;
                if (localDataContext == DependencyProperty.UnsetValue)
                {
                    popupChild.ClearValue(FrameworkElement.DataContextProperty);
                }

                foreach (var state in itemControls)
                {
                    RestoreLocalValue(
                        state.ItemsControl,
                        VirtualizingPanel.IsVirtualizingProperty,
                        state.IsVirtualizing);
                    RestoreLocalValue(
                        state.ItemsControl,
                        ScrollViewer.CanContentScrollProperty,
                        state.CanContentScroll);
                }
            }
        }
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RestoreLocalValue(
        DependencyObject target,
        DependencyProperty property,
        object value)
    {
        if (value == DependencyProperty.UnsetValue)
        {
            target.ClearValue(property);
            return;
        }

        target.SetValue(property, value);
    }

    private static Point? GetScreenRelativeOrigin(
        FrameworkElement root,
        FrameworkElement popupChild)
    {
        try
        {
            var rootVisual = Window.GetWindow(root)?.Content as Visual ?? root;
            var popupOrigin = popupChild.PointToScreen(new Point());
            return rootVisual.PointFromScreen(popupOrigin);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Point? GetFlyoutOrigin(
        FrameworkElement root,
        Flyout flyout,
        Size layerSize)
    {
        flyout.ApplyTemplate();
        if (flyout.Template.FindName("PART_Popup", flyout) is not Popup popup ||
            popup.PlacementTarget is not UIElement target)
        {
            return null;
        }

        var rootVisual = Window.GetWindow(root)?.Content as Visual ?? root;
        var targetScreenOrigin = target.PointToScreen(new Point());
        var targetOrigin = rootVisual.PointFromScreen(targetScreenOrigin);
        var targetSize = target.RenderSize;
        var preferredOrigin = GetPlacementOrigin(
            flyout.Placement,
            targetOrigin,
            targetSize,
            layerSize,
            popup.HorizontalOffset,
            popup.VerticalOffset);
        var viewport = rootVisual is FrameworkElement rootElement
            ? new Rect(new Point(), rootElement.RenderSize)
            : Rect.Empty;
        if (FitsPrimaryAxis(flyout.Placement, preferredOrigin, layerSize, viewport))
        {
            return AdjustPerpendicularAxis(
                flyout.Placement,
                preferredOrigin,
                layerSize,
                viewport);
        }

        var oppositePlacement = flyout.Placement switch
        {
            PlacementMode.Top => PlacementMode.Bottom,
            PlacementMode.Bottom => PlacementMode.Top,
            PlacementMode.Left => PlacementMode.Right,
            PlacementMode.Right => PlacementMode.Left,
            _ => flyout.Placement
        };
        var oppositeOrigin = GetPlacementOrigin(
            oppositePlacement,
            targetOrigin,
            targetSize,
            layerSize,
            popup.HorizontalOffset,
            popup.VerticalOffset);
        return FitsPrimaryAxis(oppositePlacement, oppositeOrigin, layerSize, viewport)
            ? AdjustPerpendicularAxis(oppositePlacement, oppositeOrigin, layerSize, viewport)
            : preferredOrigin;
    }

    private static bool FitsPrimaryAxis(
        PlacementMode placement,
        Point origin,
        Size layerSize,
        Rect viewport) =>
        placement is PlacementMode.Left or PlacementMode.Right
            ? origin.X >= viewport.Left && origin.X + layerSize.Width <= viewport.Right
            : origin.Y >= viewport.Top && origin.Y + layerSize.Height <= viewport.Bottom;

    private static Point AdjustPerpendicularAxis(
        PlacementMode placement,
        Point origin,
        Size layerSize,
        Rect viewport)
    {
        if (placement is PlacementMode.Left or PlacementMode.Right)
        {
            return layerSize.Height <= viewport.Height
                ? new Point(origin.X, Math.Clamp(origin.Y, viewport.Top, viewport.Bottom - layerSize.Height))
                : origin;
        }

        return layerSize.Width <= viewport.Width
            ? new Point(Math.Clamp(origin.X, viewport.Left, viewport.Right - layerSize.Width), origin.Y)
            : origin;
    }

    private static Point GetPlacementOrigin(
        PlacementMode placement,
        Point targetOrigin,
        Size targetSize,
        Size layerSize,
        double horizontalOffset,
        double verticalOffset) =>
        placement switch
        {
            PlacementMode.Top => new Point(
                targetOrigin.X + horizontalOffset,
                targetOrigin.Y - layerSize.Height + verticalOffset),
            PlacementMode.Bottom => new Point(
                targetOrigin.X + horizontalOffset,
                targetOrigin.Y + targetSize.Height + verticalOffset),
            PlacementMode.Left => new Point(
                targetOrigin.X - layerSize.Width + horizontalOffset,
                targetOrigin.Y + verticalOffset),
            PlacementMode.Right => new Point(
                targetOrigin.X + targetSize.Width + horizontalOffset,
                targetOrigin.Y + verticalOffset),
            _ => new Point(
                targetOrigin.X + horizontalOffset,
                targetOrigin.Y + targetSize.Height + verticalOffset)
        };

    private static void EnsureVisiblePixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] > 0)
            {
                return;
            }
        }

        if (pixels.Length > 0)
        {
            throw new InvalidOperationException("Transient popup capture did not contain visible pixels.");
        }
    }
}

internal sealed record TransientPopupLayer(Point Origin, Size Size, BitmapSource Bitmap);

file sealed record ItemsControlCaptureState(
    ItemsControl ItemsControl,
    object IsVirtualizing,
    object CanContentScroll);
