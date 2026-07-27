using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.App.Shared.Presentation.Selection;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Player;

public sealed class PlayerActiveCacheSelectionControllerTests
{
    [Fact]
    public void Selection_mode_reuses_desktop_click_range_select_all_and_escape_semantics()
    {
        var controller = CreateController();
        controller.SetChapters([0, 1, 2, 3]);

        Assert.False(controller.HandleChapterClick(2, DesktopSelectionModifiers.None));

        controller.EnterSelectionMode();
        Assert.False(controller.CanStart);
        controller.HandleChapterClick(1, DesktopSelectionModifiers.None);
        controller.HandleChapterClick(3, DesktopSelectionModifiers.Shift);

        Assert.Equal([1, 2, 3], controller.SelectedChapterIndices);
        Assert.Equal("已选择 3 章", controller.SelectionSummary);

        controller.SelectAll();
        Assert.Equal([0, 1, 2, 3], controller.SelectedChapterIndices);
        Assert.True(controller.ExitSelectionMode());
        Assert.Empty(controller.SelectedChapterIndices);
        Assert.False(controller.HandleChapterClick(2, DesktopSelectionModifiers.None));
    }

    [Fact]
    public void Active_batch_disables_start_and_projects_an_explicit_status()
    {
        var coordinator = new FakeActiveCacheCoordinator(CreateSnapshot(ActiveCacheBatchStatus.Running));
        var controller = new PlayerActiveCacheSelectionController(coordinator);
        controller.SetChapters([0, 1]);
        controller.EnterSelectionMode();
        controller.HandleChapterClick(0, DesktopSelectionModifiers.None);

        Assert.False(controller.CanStart);
        Assert.Contains("已有主动缓存批次", controller.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_submits_selected_chapters_and_exits_temporary_mode()
    {
        var coordinator = new FakeActiveCacheCoordinator(null);
        var controller = new PlayerActiveCacheSelectionController(coordinator);
        controller.SetChapters([0, 1, 2]);
        controller.EnterSelectionMode();
        controller.HandleChapterClick(0, DesktopSelectionModifiers.None);
        controller.HandleChapterClick(2, DesktopSelectionModifiers.Control);

        var result = await controller.StartAsync("book-1", 12, CancellationToken.None);

        Assert.True(result!.IsAccepted);
        Assert.Equal([0, 2], coordinator.LastRequest!.ChapterIndices);
        Assert.Equal(12, coordinator.LastRequest.SpeakSpeed);
        Assert.False(controller.IsSelectionMode);
    }

    private static PlayerActiveCacheSelectionController CreateController() =>
        new(new FakeActiveCacheCoordinator(null));

    private static ActiveCacheSnapshot CreateSnapshot(ActiveCacheBatchStatus status) =>
        new(
            Guid.NewGuid(),
            "book-1",
            "测试书",
            status,
            0,
            1,
            0,
            1,
            0,
            "第一章",
            [new ActiveCacheChapterSnapshot(0, "第一章", 0, 1, ActiveCacheChapterStatus.Running, null)],
            null);

    private sealed class FakeActiveCacheCoordinator(ActiveCacheSnapshot? snapshot) : IActiveCacheCoordinator
    {
        public ActiveCacheSnapshot? CurrentSnapshot { get; private set; } = snapshot;

        public StartActiveCacheRequest? LastRequest { get; private set; }

        public event EventHandler<ActiveCacheSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task<ActiveCacheStartResult> StartAsync(
            StartActiveCacheRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(new ActiveCacheStartResult(
                ActiveCacheStartStatus.Accepted,
                Guid.NewGuid(),
                null));
        }

        public Task CancelAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WaitForCurrentBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
