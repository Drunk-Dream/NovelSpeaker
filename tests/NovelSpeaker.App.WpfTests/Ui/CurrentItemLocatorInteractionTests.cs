using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovelSpeaker.App.Shared.Presentation.Scrolling;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CurrentItemLocatorInteractionTests
{
    [Fact]
    public void Locator_tracks_user_visibility_virtualization_and_current_item_changes()
    {
        WpfTestHost.RunInSta(() =>
        {
            var items = Enumerable.Range(0, 180).Select(index => $"第 {index + 1} 章").ToArray();
            object? currentItem = items[90];
            var isLocatorVisible = false;
            var listBox = new ListBox
            {
                ItemsSource = items
            };
            ScrollViewer.SetCanContentScroll(listBox, true);
            VirtualizingPanel.SetIsVirtualizing(listBox, true);
            VirtualizingPanel.SetVirtualizationMode(listBox, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(listBox, ScrollUnit.Pixel);

            var window = new Window
            {
                Width = 420,
                Height = 320,
                Content = listBox,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            var interaction = new CurrentItemLocatorInteraction(
                listBox,
                listBox.Dispatcher,
                () => currentItem,
                () => listBox.IsLoaded && listBox.ActualHeight > 0,
                () => false,
                () => TimeSpan.FromMilliseconds(120),
                value => isLocatorVisible = value);

            try
            {
                window.Show();
                DoEvents();
                interaction.OnLoaded();
                WaitUntil(
                    () => listBox.ItemContainerGenerator.ContainerFromItem(currentItem) is FrameworkElement,
                    TimeSpan.FromSeconds(1));

                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                    VisualTreeTestHelper.FindDescendant<ScrollViewer>(listBox));
                interaction.BeginContinuousUserScroll();
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + 1);
                DoEvents();
                Assert.False(isLocatorVisible);

                scrollViewer.ScrollToBottom();
                DoEvents();
                Assert.True(isLocatorVisible);

                listBox.ScrollIntoView(currentItem);
                DoEvents();
                Assert.False(isLocatorVisible);

                scrollViewer.ScrollToBottom();
                DoEvents();
                Assert.True(isLocatorVisible);
                interaction.EndContinuousUserScroll();
                Assert.Null(listBox.ItemContainerGenerator.ContainerFromItem(currentItem));

                interaction.LocateCurrentItem();
                WaitUntil(() => interaction.HasActiveAnimation, TimeSpan.FromSeconds(1));
                WaitUntil(() => !interaction.HasActiveAnimation, TimeSpan.FromSeconds(2));
                DoEvents();

                Assert.False(isLocatorVisible);
                Assert.NotNull(listBox.ItemContainerGenerator.ContainerFromItem(currentItem));

                interaction.NotifyUserScrollInput();
                scrollViewer.ScrollToBottom();
                DoEvents();
                Assert.True(isLocatorVisible);

                currentItem = items[20];
                interaction.NotifyCurrentItemChanged(animate: false);
                WaitUntil(
                    () => listBox.ItemContainerGenerator.ContainerFromItem(currentItem) is FrameworkElement,
                    TimeSpan.FromSeconds(1));

                Assert.False(isLocatorVisible);
                Assert.NotNull(listBox.ItemContainerGenerator.ContainerFromItem(currentItem));
            }
            finally
            {
                interaction.OnUnloaded();
                window.Close();
            }
        });
    }

    private static void WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (predicate())
            {
                return;
            }

            DoEvents();
        }

        DoEvents();
        Assert.True(predicate());
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
