using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NovelSpeaker.App.Shared.Presentation.Behaviors;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class WpfUiFlyoutPlacementTests
{
    [Fact]
    public async Task Flyout_uses_requested_anchor_and_restores_focus_after_escape()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var target = new Button { Content = "打开", Width = 80, Height = 32 };
            var firstAction = new Button { Content = "第一项" };
            var boundTitle = new System.Windows.Controls.TextBlock();
            boundTitle.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new Binding("Title"));
            var boundItems = new ListBox { Focusable = false, IsHitTestVisible = false };
            boundItems.ItemContainerStyle = new Style(typeof(ListBoxItem))
            {
                Setters = { new Setter(UIElement.FocusableProperty, false) }
            };
            VirtualizingPanel.SetIsVirtualizing(boundItems, true);
            ScrollViewer.SetCanContentScroll(boundItems, true);
            boundItems.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Items"));
            var surface = new Border
            {
                Style = Assert.IsType<Style>(
                    global::System.Windows.Application.Current.FindResource("App.Feedback.PopupSurface")),
                Child = new StackPanel
                {
                    Children =
                    {
                        boundTitle,
                        boundItems,
                        firstAction,
                        new Button { Content = "第二项" }
                    }
                }
            };
            var flyout = new Flyout
            {
                Style = Assert.IsType<Style>(
                    global::System.Windows.Application.Current.FindResource("App.Feedback.FlyoutHost")),
                Placement = PlacementMode.Right,
                Content = surface
            };
            WpfUiFlyoutPlacement.SetPlacementTarget(flyout, target);
            WpfUiFlyoutPlacement.SetHorizontalOffset(flyout, 12);

            var layout = new StackPanel();
            layout.Children.Add(target);
            layout.Children.Add(flyout);
            var window = new Window
            {
                Width = 480,
                Height = 320,
                Content = layout,
                DataContext = new { Title = "绑定标题", Items = new[] { "章节一", "章节二" } },
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            using var host = WpfWindowHost.Show(window);
            window.UpdateLayout();
            window.Activate();

            flyout.ApplyTemplate();
            var popup = Assert.IsType<Popup>(flyout.Template.FindName("PART_Popup", flyout));
            Assert.Same(target, popup.PlacementTarget);
            Assert.Equal(12, popup.HorizontalOffset);
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(surface));

            Assert.True(target.Focus());
            var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            flyout.Opened += (_, _) => opened.TrySetResult(true);
            flyout.IsOpen = true;
            await opened.Task;
            await DrainDispatcherAsync(window.Dispatcher);

            Assert.True(popup.AllowsTransparency);
            Assert.Null(flyout.Effect);
            Assert.Equal(Brushes.Transparent, flyout.Background);
            Assert.Equal(Brushes.Transparent, flyout.BorderBrush);
            Assert.Equal(0, flyout.BorderThickness.Left);
            var popupChild = Assert.IsAssignableFrom<DependencyObject>(popup.Child);
            var popupBorders = (popupChild is Border rootBorder
                    ? new[] { rootBorder }
                    : Enumerable.Empty<Border>())
                .Concat(FindDescendants<Border>(popupChild))
                .ToArray();
            var surfaceBorders = popupBorders
                .Where(border => ReferenceEquals(border.Style, surface.Style))
                .ToArray();
            Assert.Single(surfaceBorders);
            Assert.Same(surface, surfaceBorders[0]);
            Assert.All(
                FindVisualAncestors(surface).OfType<Border>(),
                border =>
                {
                    Assert.True(IsTransparent(border.Background));
                    Assert.True(IsTransparent(border.BorderBrush));
                    Assert.Null(border.Effect);
                });

            Assert.Same(
                firstAction,
                FocusManager.GetFocusedElement(FocusManager.GetFocusScope(firstAction)));
            var popupLayer = Assert.Single(
                TransientPopupVisualRenderer.CaptureOpenLayers(window, 96));
            Assert.True(popupLayer.Size.Width > 0);
            Assert.True(popupLayer.Size.Height > 0);
            Assert.Equal("绑定标题", boundTitle.Text);
            Assert.Equal(2, boundItems.Items.Count);
            Assert.NotNull(boundItems.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(boundItems));
            Assert.True(ScrollViewer.GetCanContentScroll(boundItems));
            Assert.Equal(
                DependencyProperty.UnsetValue,
                surface.ReadLocalValue(FrameworkElement.DataContextProperty));
            var escape = new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window),
                Environment.TickCount,
                Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            flyout.RaiseEvent(escape);
            await DrainDispatcherAsync(window.Dispatcher);

            Assert.True(escape.Handled);
            Assert.False(flyout.IsOpen);
            Assert.Same(
                target,
                FocusManager.GetFocusedElement(FocusManager.GetFocusScope(target)));
        });
    }

    [Fact]
    public async Task Flyout_uses_window_focus_fallback_when_anchor_becomes_unavailable()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var target = new Button { Content = "打开" };
            var fallback = new Button { Content = "回退" };
            var flyoutAction = new Button { Content = "浮层操作" };
            var flyout = new Flyout
            {
                Style = Assert.IsType<Style>(
                    global::System.Windows.Application.Current.FindResource("App.Feedback.FlyoutHost")),
                Content = new Border
                {
                    Style = Assert.IsType<Style>(
                        global::System.Windows.Application.Current.FindResource("App.Feedback.PopupSurface")),
                    Child = flyoutAction
                }
            };
            WpfUiFlyoutPlacement.SetPlacementTarget(flyout, target);

            var layout = new StackPanel();
            layout.Children.Add(target);
            layout.Children.Add(fallback);
            layout.Children.Add(flyout);
            var window = new Window
            {
                Width = 480,
                Height = 320,
                Content = layout,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            using var host = WpfWindowHost.Show(window);
            window.UpdateLayout();
            window.Activate();

            Assert.True(target.Focus());
            var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            flyout.Opened += (_, _) => opened.TrySetResult(true);
            flyout.IsOpen = true;
            await opened.Task;
            await DrainDispatcherAsync(window.Dispatcher);
            Assert.True(flyoutAction.IsKeyboardFocusWithin);

            var escape = new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window),
                Environment.TickCount,
                Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            flyout.RaiseEvent(escape);
            target.Visibility = Visibility.Collapsed;
            await DrainDispatcherAsync(window.Dispatcher);

            Assert.True(escape.Handled);
            Assert.False(flyout.IsOpen);
            Assert.NotSame(
                target,
                FocusManager.GetFocusedElement(FocusManager.GetFocusScope(target)));
            Assert.Same(
                fallback,
                FocusManager.GetFocusedElement(FocusManager.GetFocusScope(fallback)));
        });
    }

    private static async Task DrainDispatcherAsync(Dispatcher dispatcher)
    {
        await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
    }

    private static IReadOnlyList<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var matches = new List<T>();
        Visit(root, matches);
        return matches;

        static void Visit(DependencyObject current, ICollection<T> matches)
        {
            if (current is T match)
            {
                matches.Add(match);
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                Visit(VisualTreeHelper.GetChild(current, index), matches);
            }
        }
    }

    private static IEnumerable<DependencyObject> FindVisualAncestors(DependencyObject element)
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }

    private static bool IsTransparent(Brush? brush) =>
        brush is null || brush is SolidColorBrush { Color.A: 0 };
}
