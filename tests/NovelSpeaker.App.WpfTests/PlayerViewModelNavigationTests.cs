using System.Collections.Specialized;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Common;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests;

[Collection("WpfDispatcher")]
public sealed partial class PlayerViewModelTests
{
    [Fact]
    public async Task LoadAsync_uses_persisted_global_speed_after_restart()
    {
        var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle);
        var settingsService = new FakeAppSettingsService(
            AppSettings.Default with { DefaultSpeakSpeed = 18 });
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent(
                    "book-1",
                    "示例小说",
                    [PlaybackChapterContent.FromLoaded(0, "第一章", [])],
                    "作者甲"),
                PlaybackChapterContent.FromLoaded(
                    0,
                    "第一章",
                    [new SpeechSegment(0, 0, 3, "第一段", "第一段")])),
            settingsService: settingsService);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(18, viewModel.SpeakSpeed);
        Assert.Equal("18", viewModel.SpeedEditorText);

        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.OpenPaused),
            CancellationToken.None);

        Assert.Equal(18, coordinator.LastOpenPausedRequest!.SpeakSpeedOverride);
        Assert.Equal(18, viewModel.SpeakSpeed);
    }

    [Fact]
    public async Task HandleNavigationAsync_open_paused_calls_coordinator_for_different_book_and_loads_projection()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            2,
            1,
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false,
            "作者甲"));
        var contentService = new FakeBookPlaybackContentService(
            new PlaybackBookContent(
                "book-2",
                "另一本书",
                [PlaybackChapterContent.FromLoaded(0, "第二章", [])],
                "作者乙"),
            PlaybackChapterContent.FromLoaded(
                0,
                "第二章",
                [new SpeechSegment(0, 0, 3, "第二章第一段", "第二章第一段")]));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-2", PlayerNavigationMode.OpenPaused),
            CancellationToken.None);

        Assert.Equal(1, coordinator.OpenPausedCallCount);
        Assert.Equal("book-2", coordinator.LastOpenPausedRequest!.BookId);
        Assert.Equal("另一本书", viewModel.CurrentTitle);
        Assert.Equal("作者乙", viewModel.CurrentAuthor);
        Assert.Single(viewModel.Chapters);
        Assert.Single(viewModel.Segments);
        Assert.Equal("第二章第一段", viewModel.Segments[0].Text);
    }

    [Fact]
    public async Task HandleNavigationAsync_return_to_current_session_does_not_reopen_playback()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
            "book-1",
            "示例小说",
            0,
            "第一章",
            1,
            3,
            1,
            "默认规则",
            12,
            0,
            0,
            null,
            false,
            false,
            "作者甲"));
        var contentService = new FakeBookPlaybackContentService(
            new PlaybackBookContent(
                "book-1",
                "示例小说",
                [PlaybackChapterContent.FromLoaded(0, "第一章", [])],
                "作者甲"),
            PlaybackChapterContent.FromLoaded(
                0,
                "第一章",
                [
                    new SpeechSegment(0, 0, 3, "第一段", "第一段"),
                    new SpeechSegment(1, 3, 3, "第二段", "第二段"),
                    new SpeechSegment(2, 6, 3, "第三段", "第三段")
                ]));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.Equal(0, coordinator.OpenPausedCallCount);
        Assert.Equal(PlaybackState.Playing, viewModel.CurrentPlaybackState);
        Assert.Equal(1, viewModel.CurrentSegmentIndex);
        Assert.Equal(3, viewModel.CurrentChapterSegmentCount);
        Assert.True(viewModel.Segments[1].IsCurrent);
    }

    [Fact]
    public async Task HandleNavigationAsync_restored_session_projects_current_segment_without_view_lifecycle_state()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            1,
            3,
            1,
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false,
            "作者甲"));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(
                    0,
                    "第一章",
                    [
                        new SpeechSegment(0, 0, 3, "第一段", "第一段"),
                        new SpeechSegment(1, 3, 3, "第二段", "第二段"),
                        new SpeechSegment(2, 6, 3, "第三段", "第三段")
                    ])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.NotNull(viewModel.CurrentSegmentItem);
        Assert.Equal(1, viewModel.CurrentSegmentItem!.SegmentIndex);
        Assert.False(viewModel.ShowReturnToCurrentSegment);
    }

    [Fact]
    public async Task HandleNavigationAsync_targeted_chapter_on_same_playing_book_keeps_playing_and_jumps()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
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
        var contentService = new FakeBookPlaybackContentService(
            new PlaybackBookContent(
                "book-1",
                "示例小说",
                [
                    PlaybackChapterContent.FromLoaded(0, "第一章", []),
                    PlaybackChapterContent.FromLoaded(1, "第二章", [])
                ],
                "作者甲"),
            PlaybackChapterContent.FromLoaded(
                1,
                "第二章",
                [new SpeechSegment(0, 0, 4, "第二章第一段", "第二章第一段")]));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.OpenPaused, 1, 0),
            CancellationToken.None);

        Assert.Equal(1, coordinator.LastJumpedChapterIndex);
        Assert.Equal(PlaybackState.Playing, viewModel.CurrentPlaybackState);
        Assert.Equal(0, coordinator.OpenPausedCallCount);
    }

    [Fact]
    public async Task HandleNavigationAsync_targeted_chapter_on_same_paused_book_stays_paused_and_jumps()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
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
        var contentService = new FakeBookPlaybackContentService(
            new PlaybackBookContent(
                "book-1",
                "示例小说",
                [
                    PlaybackChapterContent.FromLoaded(0, "第一章", []),
                    PlaybackChapterContent.FromLoaded(1, "第二章", [])
                ],
                "作者甲"),
            PlaybackChapterContent.FromLoaded(
                1,
                "第二章",
                [new SpeechSegment(0, 0, 4, "第二章第一段", "第二章第一段")]));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.OpenPaused, 1, 0),
            CancellationToken.None);

        Assert.Equal(1, coordinator.LastJumpedChapterIndex);
        Assert.Equal(PlaybackState.Paused, viewModel.CurrentPlaybackState);
        Assert.Equal(0, coordinator.OpenPausedCallCount);
    }

    [Fact]
    public async Task HandleNavigationAsync_targeted_chapter_on_different_playing_book_restarts_playback()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
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
        var contentService = new FakeBookPlaybackContentService(
            new PlaybackBookContent("book-2", "另一本书", [PlaybackChapterContent.FromLoaded(1, "第二章", [])], "作者乙"),
            PlaybackChapterContent.FromLoaded(1, "第二章", [new SpeechSegment(0, 0, 4, "第二章第一段", "第二章第一段")]));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-2", PlayerNavigationMode.OpenPaused, 1, 0),
            CancellationToken.None);

        Assert.NotNull(coordinator.LastStartRequest);
        Assert.Equal("book-2", coordinator.LastStartRequest!.BookId);
        Assert.Equal(1, coordinator.LastStartRequest.ChapterIndex);
        Assert.Equal(0, coordinator.LastStartRequest.SegmentIndex);
        Assert.Equal(PlaybackState.Playing, viewModel.CurrentPlaybackState);
    }

    [Fact]
    public async Task HandleNavigationAsync_targeted_chapter_on_different_paused_book_opens_paused()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
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
        var contentService = new FakeBookPlaybackContentService(
            new PlaybackBookContent("book-2", "另一本书", [PlaybackChapterContent.FromLoaded(1, "第二章", [])], "作者乙"),
            PlaybackChapterContent.FromLoaded(1, "第二章", [new SpeechSegment(0, 0, 4, "第二章第一段", "第二章第一段")]));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-2", PlayerNavigationMode.OpenPaused, 1, 0),
            CancellationToken.None);

        Assert.Null(coordinator.LastStartRequest);
        Assert.Equal(1, coordinator.OpenPausedCallCount);
        Assert.Equal(1, coordinator.LastOpenPausedRequest!.ChapterIndex);
        Assert.Equal(0, coordinator.LastOpenPausedRequest.SegmentIndex);
        Assert.Equal(PlaybackState.Paused, viewModel.CurrentPlaybackState);
    }

    [Fact]
    public async Task HandleNavigationAsync_missing_book_navigates_to_library_and_warns()
    {
        var navigationService = new FakeNavigationService();
        var feedbackService = new FakeAppFeedbackService();
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(),
            new FakeBookPlaybackContentService(null, null),
            navigationService: navigationService,
            feedbackService: feedbackService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("missing-book", PlayerNavigationMode.OpenPaused),
            CancellationToken.None);

        Assert.Equal(typeof(LibraryPage), navigationService.LastNavigationPageType);
        Assert.Equal("无法打开书籍", feedbackService.LastWarningTitle);
    }

    [Fact]
    public async Task SelectChapterCommand_jumps_without_reopening_playback()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            2,
            1,
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent(
                    "book-1",
                    "示例小说",
                    [
                        PlaybackChapterContent.FromLoaded(0, "第一章", []),
                        PlaybackChapterContent.FromLoaded(1, "第二章", [])
                    ],
                    "作者甲"),
                PlaybackChapterContent.FromLoaded(1, "第二章", [new SpeechSegment(0, 0, 4, "第二章第一段", "第二章第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await viewModel.SelectChapterCommand.ExecuteAsync(viewModel.Chapters[1]);

        Assert.Equal(1, coordinator.LastJumpedChapterIndex);
        Assert.Equal(0, coordinator.OpenPausedCallCount);
    }

    [Fact]
    public async Task SelectChapterCommand_current_chapter_resumes_without_jump()
    {
        var autoScrollCoordinator = new FakePlayerAutoScrollCoordinator();
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
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
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            autoScrollCoordinator: autoScrollCoordinator);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.NotifyUserScrollInput();
        await viewModel.SelectChapterCommand.ExecuteAsync(viewModel.CurrentChapterItem);

        Assert.Null(coordinator.LastJumpedChapterIndex);
        Assert.Equal(PlayerAutoScrollState.AutoCentering, viewModel.AutoScrollState);
        Assert.Equal(1, autoScrollCoordinator.ResumeAutoCenterCallCount);
    }

    [Fact]
    public async Task Snapshot_updates_ignore_stale_chapter_load_results()
    {
        var firstChapterLoad = new TaskCompletionSource<PlaybackChapterContent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var contentService = new DelayedBookPlaybackContentService(
            new PlaybackBookContent(
                "book-1",
                "示例小说",
                [
                    PlaybackChapterContent.FromLoaded(0, "第一章", []),
                    PlaybackChapterContent.FromLoaded(1, "第二章", [])
                ],
                "作者甲"),
            [
                firstChapterLoad.Task,
                Task.FromResult<PlaybackChapterContent?>(PlaybackChapterContent.FromLoaded(
                    1,
                    "第二章",
                    [new SpeechSegment(0, 0, 4, "第二章第一段", "第二章第一段")]))
            ]);
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
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
            false));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        var navigationTask = viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await contentService.WaitForChapterRequestCountAsync(1);

        coordinator.Publish(coordinator.CurrentSnapshot with
        {
            ChapterIndex = 1,
            ChapterTitle = "第二章",
            SegmentIndex = 0,
            SegmentCount = 1
        });

        await contentService.WaitForChapterRequestCountAsync(2);
        firstChapterLoad.SetResult(PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [new SpeechSegment(0, 0, 4, "第一章第一段", "第一章第一段")]));

        await navigationTask;

        Assert.Equal(1, viewModel.CurrentChapterIndex);
        Assert.Single(viewModel.Segments);
        Assert.Equal("第二章第一段", viewModel.Segments[0].Text);
    }

    [Fact]
    public async Task Same_chapter_snapshot_sequence_preserves_segment_items_while_updating_current_segment()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Paused,
                "book-1",
                "示例小说",
                0,
                "第一章",
                0,
                3,
                1,
                "默认规则",
                10,
                0,
                0,
                null,
                false,
                false,
                "作者甲"));
            var viewModel = CreateViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                    PlaybackChapterContent.FromLoaded(
                        0,
                        "第一章",
                        [
                            new SpeechSegment(0, 0, 4, "第一段", "第一段"),
                            new SpeechSegment(1, 4, 4, "第二段", "第二段"),
                            new SpeechSegment(2, 8, 4, "第三段", "第三段")
                        ])));

            await viewModel.LoadAsync(CancellationToken.None);
            await viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None);

            var originalItems = viewModel.Segments.ToArray();
            var collectionChanges = new List<NotifyCollectionChangedAction>();
            viewModel.Segments.CollectionChanged += (_, eventArgs) => collectionChanges.Add(eventArgs.Action);

            coordinator.Publish(coordinator.CurrentSnapshot with
            {
                State = PlaybackState.Buffering,
                SegmentIndex = 1,
                SegmentCount = 3
            });
            coordinator.Publish(coordinator.CurrentSnapshot with { State = PlaybackState.Preparing });
            coordinator.Publish(coordinator.CurrentSnapshot with { State = PlaybackState.Playing });

            Assert.Empty(collectionChanges);
            Assert.Equal(originalItems.Length, viewModel.Segments.Count);
            Assert.All(originalItems.Select((item, index) => (item, index)), pair =>
                Assert.Same(pair.item, viewModel.Segments[pair.index]));
            Assert.Same(originalItems[1], viewModel.CurrentSegmentItem);
            Assert.True(viewModel.Segments[1].IsCurrent);
            Assert.False(viewModel.Segments[0].IsCurrent);
        });
    }

}
