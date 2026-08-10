using System.Windows.Threading;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App.Bootstrap;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Bootstrap;

[Collection("WpfDispatcher")]
public sealed class WpfStartupRuntimeTests
{
    [Fact]
    public async Task Shutdown_cancels_chapter_export_before_stopping_cache_background_work()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var events = new List<string>();
            var export = new FakeChapterExportCoordinator(Task.CompletedTask, events);
            var cache = new FakeCacheWorkspaceBackgroundTaskOwner(events);
            var runtime = new WpfStartupRuntime(
                Dispatcher.CurrentDispatcher,
                _ => { },
                TimeSpan.FromSeconds(1));

            await runtime.WaitForBackgroundTasksAsync(export, cache, CancellationToken.None);

            Assert.Equal(1, export.CancelCallCount);
            Assert.Equal(["export", "cache"], events);
        });
    }

    [Fact]
    public async Task Shutdown_continues_to_cache_cleanup_when_export_cancel_exceeds_bound()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var exportCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new List<string>();
            var export = new FakeChapterExportCoordinator(exportCancellation.Task, events);
            var cache = new FakeCacheWorkspaceBackgroundTaskOwner(events);
            var runtime = new WpfStartupRuntime(
                Dispatcher.CurrentDispatcher,
                _ => { },
                TimeSpan.FromMilliseconds(50));

            await runtime.WaitForBackgroundTasksAsync(export, cache, CancellationToken.None);

            Assert.Equal(1, export.CancelCallCount);
            Assert.Equal(["export", "cache"], events);
            exportCancellation.TrySetResult();
        });
    }

    private sealed class FakeChapterExportCoordinator(
        Task cancelTask,
        List<string> events) : IChapterExportCoordinator
    {
        public int CancelCallCount { get; private set; }

        public ChapterExportSnapshot? CurrentSnapshot => null;

        public event EventHandler<ChapterExportSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task<ChapterExportStartResult> StartAsync(
            StartChapterExportRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancelCallCount++;
            events.Add("export");
            return cancelTask;
        }

        public Task WaitForCurrentBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCacheWorkspaceBackgroundTaskOwner(List<string> events) : ICacheWorkspaceBackgroundTaskOwner
    {
        public Task StopBackgroundOperationsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("cache");
            return Task.CompletedTask;
        }
    }
}
