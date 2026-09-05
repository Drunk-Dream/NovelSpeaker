using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Scrolling;
using NovelSpeaker.TestKit.Wpf;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Shared;

[Collection("WpfDispatcher")]
public sealed class WpfUiSchedulerTests
{
    [Fact]
    public async Task InvokeLaterAsync_executes_zero_argument_action_on_the_dispatcher()
    {
        var executed = false;

        await WpfTestHost.RunInStaAsync(async () =>
        {
            var scheduler = new WpfUiScheduler(Dispatcher.CurrentDispatcher);
            var scheduled = scheduler.InvokeLaterAsync(() => executed = true);
            Assert.False(executed);
            await scheduled;
        });

        Assert.True(executed);
    }

    [Fact]
    public async Task InvokeLaterAsync_honors_cancellation_before_posting()
    {
        var executed = false;
        using var cancellation = new CancellationTokenSource();

        await WpfTestHost.RunInStaAsync(async () =>
        {
            var scheduler = new WpfUiScheduler(Dispatcher.CurrentDispatcher);
            var scheduled = scheduler.InvokeLaterAsync(() => executed = true, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduled);
        });

        Assert.False(executed);
    }

    [Fact]
    public async Task Staged_catalog_reconciliation_notifies_realized_wpf_rows_after_interleaved_updates()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var rows = new ResettableObservableCollection<string>();
            rows.ReplaceWith(Enumerable.Range(0, 10_000).Select(static _ => "旧状态").ToArray());
            var resetCount = 0;
            rows.CollectionChanged += (_, args) =>
            {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    resetCount++;
                }
            };
            var listBox = new ListBox
            {
                ItemsSource = rows,
                Height = 120,
                Width = 320
            };
            var window = new Window
            {
                Content = listBox,
                Width = 360,
                Height = 180,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };

            using var host = WpfWindowHost.Show(window);
            window.UpdateLayout();
            var firstBatchContent = string.Empty;
            var scheduler = new InterleavingUiScheduler(
                new WpfUiScheduler(Dispatcher.CurrentDispatcher),
                rows,
                () =>
                {
                    listBox.UpdateLayout();
                    firstBatchContent = Assert.IsType<ListBoxItem>(
                        listBox.ItemContainerGenerator.ContainerFromIndex(0)).Content?.ToString() ?? string.Empty;
                });

            await rows.ReplaceWithInBatchesAsync(
                Enumerable.Range(0, 10_000).ToArray(),
                static _ => "分批状态",
                scheduler,
                CancellationToken.None,
                batchSize: 256);

            Assert.Equal("分批状态", firstBatchContent);
            Assert.True(resetCount >= 40);
            Assert.Equal("中间状态", rows[0]);
            rows.ReplaceAt(0, "最终状态");
            window.UpdateLayout();

            var firstContainer = Assert.IsType<ListBoxItem>(listBox.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal("最终状态", firstContainer.Content);
        });
    }

    private sealed class InterleavingUiScheduler(
        WpfUiScheduler inner,
        ResettableObservableCollection<string> rows,
        Action captureFirstBatch) : IUiScheduler
    {
        private int _invocation;

        public bool CheckAccess() => inner.CheckAccess();

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) =>
            inner.InvokeAsync(action, cancellationToken);

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
            inner.InvokeAsync(action, cancellationToken);

        public async Task InvokeLaterAsync(Action action, CancellationToken cancellationToken = default)
        {
            await inner.InvokeLaterAsync(action, cancellationToken);
            if (Interlocked.Increment(ref _invocation) == 2)
            {
                captureFirstBatch();
                rows.ReplaceAt(0, "中间状态");
            }
        }
    }

    [Fact]
    public async Task Virtualized_catalog_viewport_returns_a_bounded_window_near_the_scrolled_tail()
    {
        await WpfTestHost.RunInStaAsync(() =>
        {
            var listBox = new ListBox
            {
                ItemsSource = Enumerable.Range(0, 10_000).ToArray(),
                Height = 120,
                Width = 320
            };
            var window = new Window
            {
                Content = listBox,
                Width = 360,
                Height = 180,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };

            using var host = WpfWindowHost.Show(window);
            window.UpdateLayout();
            var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                VisualTreeTestHelper.FindDescendant<ScrollViewer>(listBox));
            scrollViewer.ScrollToEnd();
            window.UpdateLayout();

            var (start, count) = VirtualizedCatalogViewport.GetWindow(
                listBox,
                scrollViewer,
                itemCount: 10_000,
                windowSize: 32);

            Assert.InRange(start, 9_960, 9_999);
            Assert.InRange(count, 1, 32);
            Assert.True(start + count <= 10_000);

            return Task.CompletedTask;
        });
    }
}
