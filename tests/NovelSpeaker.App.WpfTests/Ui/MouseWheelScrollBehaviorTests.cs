using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NovelSpeaker.App.Shell.Input;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class MouseWheelScrollBehaviorTests
{
    [Fact]
    public void MouseWheelScrollBehavior_scrolls_scrollviewer_when_wheel_originates_from_child()
    {
        WpfTestHost.RunInSta(() =>
        {
            var contentPanel = new StackPanel();
            var wheelSource = new Button
            {
                Content = "child",
                Height = 32,
                Margin = new Thickness(0, 0, 0, 12)
            };
            contentPanel.Children.Add(wheelSource);

            for (var index = 0; index < 20; index++)
            {
                contentPanel.Children.Add(new Border
                {
                    Height = 32,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }

            var scrollViewer = new ScrollViewer
            {
                Width = 240,
                Height = 180,
                Content = contentPanel
            };
            MouseWheelScrollBehavior.SetEnabled(scrollViewer, true);

            var window = new Window
            {
                Width = 260,
                Height = 220,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = scrollViewer
            };

            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                Assert.True(scrollViewer.ScrollableHeight > 0);
                var handled = MouseWheelScrollBehavior.HandlePreviewMouseWheel(
                    scrollViewer,
                    wheelSource,
                    -Mouse.MouseWheelDeltaForOneLine);

                Assert.True(handled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MouseWheelScrollBehavior_scrolls_virtualized_list_by_logical_lines()
    {
        WpfTestHost.RunInSta(() =>
        {
            var listBox = new ListBox
            {
                Width = 240,
                Height = 180,
                ItemsSource = Enumerable.Range(0, 100).Select(index => $"Chapter {index + 1}").ToArray()
            };
            ScrollViewer.SetCanContentScroll(listBox, true);
            VirtualizingPanel.SetIsVirtualizing(listBox, true);
            VirtualizingPanel.SetScrollUnit(listBox, ScrollUnit.Item);
            VirtualizingPanel.SetVirtualizationMode(listBox, VirtualizationMode.Recycling);

            var window = new Window
            {
                Width = 260,
                Height = 220,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = listBox
            };

            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                    VisualTreeTestHelper.FindDescendant<ScrollViewer>(listBox));
                Assert.True(scrollViewer.CanContentScroll);
                Assert.True(scrollViewer.ScrollableHeight > 0);

                var initialOffset = scrollViewer.VerticalOffset;
                var handled = MouseWheelScrollBehavior.HandlePreviewMouseWheel(
                    scrollViewer,
                    listBox,
                    -Mouse.MouseWheelDeltaForOneLine);
                window.UpdateLayout();

                var expectedLineCount = SystemParameters.WheelScrollLines < 0
                    ? Math.Max(1d, Math.Round(scrollViewer.ViewportHeight * 0.9d))
                    : Math.Max(1, SystemParameters.WheelScrollLines);
                Assert.True(handled);
                Assert.InRange(scrollViewer.VerticalOffset - initialOffset, expectedLineCount - 1d, expectedLineCount + 1d);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MouseWheelScrollBehavior_is_enabled_application_wide()
    {
        WpfTestHost.RunInSta(() =>
        {
            var contentPanel = new StackPanel();
            var wheelSource = new Button
            {
                Content = "child",
                Height = 32,
                Margin = new Thickness(0, 0, 0, 12)
            };
            contentPanel.Children.Add(wheelSource);

            for (var index = 0; index < 20; index++)
            {
                contentPanel.Children.Add(new Border
                {
                    Height = 32,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }

            var scrollViewer = new ScrollViewer
            {
                Width = 240,
                Height = 180,
                Content = contentPanel
            };
            var window = new Window
            {
                Width = 260,
                Height = 220,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = scrollViewer
            };

            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                Assert.True(scrollViewer.ScrollableHeight > 0);

                var wheelEvent = new MouseWheelEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    -Mouse.MouseWheelDeltaForOneLine)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                };
                wheelSource.RaiseEvent(wheelEvent);
                window.Dispatcher.Invoke(DispatcherPriority.Background, static () => { });
                window.UpdateLayout();

                Assert.True(wheelEvent.Handled);
                Assert.True(scrollViewer.VerticalOffset > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
