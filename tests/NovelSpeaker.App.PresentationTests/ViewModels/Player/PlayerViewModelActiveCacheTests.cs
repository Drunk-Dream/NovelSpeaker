using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Speech;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels.Player;

public sealed partial class PlayerViewModelTests
{
    [Fact]
    public async Task Active_cache_selection_consumes_chapter_clicks_then_exit_restores_playback_jump()
    {
        var playback = CreatePlaybackCoordinator();
        var viewModel = CreateViewModel(playback, CreateContentService());
        await OpenBookAsync(viewModel);

        viewModel.EnterActiveCacheSelectionCommand.Execute(null);
        await viewModel.HandleChapterClickAsync(
            viewModel.Chapters[1],
            DesktopSelectionModifiers.None,
            CancellationToken.None);
        await viewModel.HandleChapterClickAsync(
            viewModel.Chapters[2],
            DesktopSelectionModifiers.Control,
            CancellationToken.None);

        Assert.Null(playback.LastJumpedChapterIndex);
        Assert.Equal(2, viewModel.SelectedActiveCacheChapterCount);
        Assert.True(viewModel.Chapters[1].IsSelectedForActiveCache);
        Assert.True(viewModel.Chapters[2].IsSelectedForActiveCache);

        Assert.True(viewModel.HandleActiveCacheEscape());
        await viewModel.HandleChapterClickAsync(
            viewModel.Chapters[1],
            DesktopSelectionModifiers.None,
            CancellationToken.None);

        Assert.Equal(1, playback.LastJumpedChapterIndex);
        Assert.False(viewModel.IsActiveCacheSelectionMode);
    }

    [Fact]
    public async Task Active_cache_selection_projects_shift_range_and_select_all_through_the_view_model()
    {
        var viewModel = CreateViewModel(
            CreatePlaybackCoordinator(),
            CreateContentService());
        await OpenBookAsync(viewModel);

        viewModel.EnterActiveCacheSelectionCommand.Execute(null);
        await viewModel.HandleChapterClickAsync(
            viewModel.Chapters[1],
            DesktopSelectionModifiers.None,
            CancellationToken.None);
        await viewModel.HandleChapterClickAsync(
            viewModel.Chapters[2],
            DesktopSelectionModifiers.Shift,
            CancellationToken.None);

        Assert.Equal(2, viewModel.SelectedActiveCacheChapterCount);
        Assert.True(viewModel.Chapters[1].IsSelectedForActiveCache);
        Assert.True(viewModel.Chapters[2].IsSelectedForActiveCache);

        Assert.True(viewModel.HandleActiveCacheSelectAll());
        Assert.Equal(3, viewModel.SelectedActiveCacheChapterCount);
        Assert.All(viewModel.Chapters, chapter => Assert.True(chapter.IsSelectedForActiveCache));

        Assert.True(viewModel.HandleActiveCacheEscape());
        Assert.DoesNotContain(viewModel.Chapters, chapter => chapter.IsSelectedForActiveCache);
    }

    [Fact]
    public async Task Active_cache_start_uses_selected_chapters_and_active_batch_blocks_another_start()
    {
        var activeCache = new FakeActiveCacheCoordinator();
        var viewModel = CreateViewModel(
            CreatePlaybackCoordinator(),
            CreateContentService(),
            activeCacheCoordinator: activeCache);
        await OpenBookAsync(viewModel);

        viewModel.EnterActiveCacheSelectionCommand.Execute(null);
        await viewModel.HandleChapterClickAsync(
            viewModel.Chapters[0],
            DesktopSelectionModifiers.None,
            CancellationToken.None);
        await viewModel.HandleChapterClickAsync(
            viewModel.Chapters[2],
            DesktopSelectionModifiers.Control,
            CancellationToken.None);

        Assert.True(viewModel.CanStartActiveCache);
        await viewModel.StartActiveCacheCommand.ExecuteAsync(null);

        Assert.Equal([0, 2], activeCache.LastRequest!.ChapterIndices);
        Assert.Equal(viewModel.SpeakSpeed, activeCache.LastRequest.SpeakSpeed);
        Assert.False(viewModel.IsActiveCacheSelectionMode);

        viewModel.EnterActiveCacheSelectionCommand.Execute(null);
        await viewModel.HandleChapterClickAsync(
            viewModel.Chapters[1],
            DesktopSelectionModifiers.None,
            CancellationToken.None);

        Assert.False(viewModel.CanStartActiveCache);
        Assert.Contains("已有主动缓存批次", viewModel.ActiveCacheStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Page_leave_releases_snapshot_subscription_and_temporary_selection_without_cancelling_batch()
    {
        var activeCache = new FakeActiveCacheCoordinator();
        var viewModel = CreateViewModel(
            CreatePlaybackCoordinator(),
            CreateContentService(),
            activeCacheCoordinator: activeCache);
        await OpenBookAsync(viewModel);
        viewModel.EnterActiveCacheSelectionCommand.Execute(null);
        Assert.Equal(1, activeCache.SubscriberCount);

        var snapshot = CreateActiveSnapshot();
        activeCache.Publish(snapshot);
        viewModel.OnPageNavigatedFrom();

        Assert.Equal(0, activeCache.SubscriberCount);
        Assert.False(viewModel.IsActiveCacheSelectionMode);
        Assert.Same(snapshot, activeCache.CurrentSnapshot);
        Assert.Equal(0, activeCache.CancelCallCount);

        viewModel.OnPageNavigatedTo(CancellationToken.None);

        Assert.Equal(1, activeCache.SubscriberCount);
        Assert.True(viewModel.HasActiveCacheBatch);
    }

    [Fact]
    public async Task Chapter_cache_percentages_refresh_on_initial_load_and_matching_cache_changes()
    {
        var cacheWorkspace = new FakeCacheWorkspaceService
        {
            Statuses =
            [
                new ChapterCacheStatus(0, 1, 4),
                new ChapterCacheStatus(1, 0, 4),
                new ChapterCacheStatus(2, 1, null)
            ]
        };
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(
            CreatePlaybackCoordinator(),
            CreateContentService(),
            settingsService: settingsService,
            cacheWorkspaceService: cacheWorkspace);

        await OpenBookAsync(viewModel);

        Assert.Equal("25%", viewModel.Chapters[0].CachePercentageText);
        Assert.Equal(string.Empty, viewModel.Chapters[1].CachePercentageText);
        Assert.Equal(string.Empty, viewModel.Chapters[2].CachePercentageText);
        Assert.Equal(1, cacheWorkspace.StatusCallCount);
        Assert.Equal(1, cacheWorkspace.SubscriberCount);

        cacheWorkspace.Statuses = [new ChapterCacheStatus(1, 3, 4)];
        cacheWorkspace.Publish(new CacheChangedEventArgs("book-1", 1));

        Assert.Equal("75%", viewModel.Chapters[1].CachePercentageText);
        Assert.Equal(2, cacheWorkspace.StatusCallCount);

        cacheWorkspace.Publish(new CacheChangedEventArgs("another-book", 1));
        Assert.Equal(2, cacheWorkspace.StatusCallCount);

        cacheWorkspace.Statuses =
        [
            new ChapterCacheStatus(0, 4, 4),
            new ChapterCacheStatus(1, 4, 4),
            new ChapterCacheStatus(2, 0, 4)
        ];
        settingsService.Publish(settingsService.Current with { DefaultSpeakSpeed = 11 });
        Assert.Equal("100%", viewModel.Chapters[0].CachePercentageText);
        Assert.Equal("100%", viewModel.Chapters[1].CachePercentageText);
        Assert.Equal(string.Empty, viewModel.Chapters[2].CachePercentageText);
        Assert.Equal(3, cacheWorkspace.StatusCallCount);

        viewModel.OnPageNavigatedFrom();
        Assert.Equal(0, cacheWorkspace.SubscriberCount);

        cacheWorkspace.Statuses = [new ChapterCacheStatus(1, 4, 4)];
        cacheWorkspace.Publish(new CacheChangedEventArgs("book-1", 1));
        Assert.Equal("100%", viewModel.Chapters[1].CachePercentageText);
        Assert.Equal(3, cacheWorkspace.StatusCallCount);
    }

    [Fact]
    public async Task Page_leave_discards_cache_status_projection_that_reaches_the_ui_late()
    {
        var cacheWorkspace = new FakeCacheWorkspaceService
        {
            Statuses = [new ChapterCacheStatus(0, 1, 1)]
        };
        var uiScheduler = new QueuedUiScheduler();
        var viewModel = CreateViewModel(
            CreatePlaybackCoordinator(),
            CreateContentService(),
            cacheWorkspaceService: cacheWorkspace,
            uiScheduler: uiScheduler);

        await OpenBookAsync(viewModel);
        Assert.Equal(1, uiScheduler.PendingCount);
        Assert.Equal(string.Empty, viewModel.Chapters[0].CachePercentageText);

        viewModel.OnPageNavigatedFrom();
        uiScheduler.RunNext();

        Assert.Equal(string.Empty, viewModel.Chapters[0].CachePercentageText);
    }

    private static FakePlaybackCoordinator CreatePlaybackCoordinator() =>
        new(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false,
            "作者甲"));

    private static FakeBookPlaybackContentService CreateContentService() =>
        new(
            new PlaybackBookContent(
                "book-1",
                "示例小说",
                [
                    PlaybackChapterContent.FromLoaded(0, "第一章", []),
                    PlaybackChapterContent.FromLoaded(1, "第二章", []),
                    PlaybackChapterContent.FromLoaded(2, "第三章", [])
                ],
                "作者甲"),
            PlaybackChapterContent.FromLoaded(
                0,
                "第一章",
                [new SpeechSegment(0, 0, 2, "第一段", "正文")]));

    private static async Task OpenBookAsync(PlayerViewModel viewModel)
    {
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);
    }

    private static ActiveCacheSnapshot CreateActiveSnapshot() =>
        new(
            Guid.NewGuid(),
            "book-1",
            "示例小说",
            ActiveCacheBatchStatus.Running,
            0,
            1,
            0,
            1,
            0,
            "第一章",
            [new ActiveCacheChapterSnapshot(0, "第一章", 0, 1, ActiveCacheChapterStatus.Running, null)],
            null);
}
