using System.Collections.Specialized;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Player;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class PlayerViewModelTests
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
        await Task.Delay(20);

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
        await Task.Delay(20);

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

    [Fact]
    public async Task SelectRuleCommand_changes_rule_without_losing_context()
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
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            ruleService: new FakeTtsRuleQueries(
                [
                    new TtsRuleSummary(1, "默认规则", true, true, null),
                    new TtsRuleSummary(2, "备用规则", true, false, null)
                ]));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules[1]);

        Assert.Equal(2, coordinator.LastChangedRuleId);
        Assert.Equal("示例小说", viewModel.CurrentTitle);
    }

    [Fact]
    public async Task SelectRuleCommand_ignores_current_rule()
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
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules[0]);

        Assert.Null(coordinator.LastChangedRuleId);
    }

    [Fact]
    public async Task ApplySpeakSpeedCommand_changes_speed_with_current_context()
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
            false));
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            settingsService: settingsService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.ToggleSpeedMenuCommand.Execute(null);
        viewModel.SpeedEditorText = "18";
        await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);

        Assert.Equal(18, coordinator.LastChangedSpeakSpeed);
        Assert.Equal(PlaybackState.Paused, viewModel.CurrentPlaybackState);
        Assert.Equal("示例小说", viewModel.CurrentTitle);
        Assert.Equal(18, settingsService.Settings.DefaultSpeakSpeed);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("20", 20)]
    public async Task ApplySpeakSpeedCommand_accepts_boundary_values(string input, int expectedSpeed)
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
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.ToggleSpeedMenuCommand.Execute(null);
        viewModel.SpeedEditorText = input;
        await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);

        Assert.Equal(expectedSpeed, coordinator.LastChangedSpeakSpeed);
        Assert.Equal(string.Empty, viewModel.SpeedEditorErrorText);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("abc")]
    public async Task ApplySpeakSpeedCommand_rejects_invalid_or_out_of_range_values(string input)
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
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.ToggleSpeedMenuCommand.Execute(null);
        viewModel.SpeedEditorText = input;
        await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);

        Assert.Null(coordinator.LastChangedSpeakSpeed);
        Assert.Contains("1 到 20", viewModel.SpeedEditorErrorText);
        Assert.Equal(PlaybackState.Paused, viewModel.CurrentPlaybackState);
    }

    [Fact]
    public async Task IncreaseAndDecreaseSpeakSpeedCommands_apply_immediately()
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
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await viewModel.IncreaseSpeakSpeedCommand.ExecuteAsync(null);
        Assert.Equal(11, coordinator.LastChangedSpeakSpeed);
        Assert.Equal("11", viewModel.SpeedEditorText);

        await viewModel.DecreaseSpeakSpeedCommand.ExecuteAsync(null);
        Assert.Equal(10, coordinator.LastChangedSpeakSpeed);
        Assert.Equal("10", viewModel.SpeedEditorText);
    }

    [Fact]
    public async Task HandleNavigationAsync_same_book_open_paused_request_keeps_real_time_session()
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
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.OpenPaused),
            CancellationToken.None);

        Assert.Equal(0, coordinator.OpenPausedCallCount);
        Assert.Equal(PlaybackState.Playing, viewModel.CurrentPlaybackState);
    }

    [Fact]
    public async Task HandleNavigationAsync_restores_paused_session_when_rule_becomes_available_again()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Stopped,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            null,
            null,
            10,
            0,
            0,
            "当前没有可用的 TTS 规则，请先前往规则页选择或导入规则。",
            false,
            false,
            false,
            "作者甲",
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            ruleService: new FakeTtsRuleQueries(
                [new TtsRuleSummary(1, "默认规则", true, true, null)]));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.Equal(1, coordinator.OpenPausedCallCount);
        Assert.True(viewModel.HasAvailableRule);
        Assert.False(viewModel.ShowNoRuleState);
        Assert.True(viewModel.ShowPlaybackControls);
        Assert.Equal(PlaybackState.Paused, viewModel.CurrentPlaybackState);
    }

    [Fact]
    public async Task Faulted_snapshot_shows_error_bar_and_retry_flow()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Faulted,
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
            "网络失败，请稍后重试。",
            false,
            true,
            false,
            "作者甲"));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.True(viewModel.ShowPlaybackErrorBar);
        Assert.False(viewModel.CanTogglePlayPause);
        Assert.Equal("网络失败，请稍后重试。", viewModel.ErrorText);

        await viewModel.RetryCurrentSegmentCommand.ExecuteAsync(null);
        Assert.Equal(1, coordinator.RetryCurrentSegmentCallCount);

        viewModel.OpenRuleMenuCommand.Execute(null);
        Assert.True(viewModel.IsRuleMenuOpen);
    }

    [Fact]
    public async Task CommitSegmentProgressAsync_same_segment_is_noop()
    {
        var autoScrollCoordinator = new FakePlayerAutoScrollCoordinator();
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
            false));
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
                    ])),
            autoScrollCoordinator: autoScrollCoordinator);
        coordinator.ReadAutoScrollStateDuringSegmentJump = () => viewModel.AutoScrollState;

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.NotifyUserScrollInput();
        viewModel.BeginSegmentProgressInteraction();
        viewModel.PreviewSegmentProgress(1);
        await viewModel.CommitSegmentProgressAsync(1, CancellationToken.None);

        Assert.Null(coordinator.LastJumpedSegmentIndex);
        Assert.Equal("2 / 3", viewModel.DisplayedSegmentCounterText);
        Assert.Equal(PlayerAutoScrollState.AutoCentering, viewModel.AutoScrollState);
        Assert.Equal(1, autoScrollCoordinator.ResumeAutoCenterCallCount);
    }

    [Fact]
    public async Task CommitSegmentProgressAsync_new_segment_jumps_once()
    {
        var autoScrollCoordinator = new FakePlayerAutoScrollCoordinator();
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
            false));
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
                    ])),
            autoScrollCoordinator: autoScrollCoordinator);
        coordinator.ReadAutoScrollStateDuringSegmentJump = () => viewModel.AutoScrollState;

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.NotifyUserScrollInput();
        viewModel.BeginSegmentProgressInteraction();
        viewModel.PreviewSegmentProgress(2);
        await viewModel.CommitSegmentProgressAsync(2, CancellationToken.None);

        Assert.Equal(2, coordinator.LastJumpedSegmentIndex);
        Assert.Equal(0, coordinator.LastJumpedSegmentChapterIndex);
        Assert.Equal(PlayerAutoScrollState.AutoCentering, coordinator.AutoScrollStateObservedDuringLastJumpToSegment);
        Assert.Equal(PlayerAutoScrollState.AutoCentering, viewModel.AutoScrollState);
        Assert.Equal(1, autoScrollCoordinator.ResumeAutoCenterCallCount);
    }

    [Fact]
    public async Task NotifyUserScrollInput_exposes_return_to_current_segment()
    {
        WpfTestHost.RunInSta(() =>
        {
            var autoScrollCoordinator = new FakePlayerAutoScrollCoordinator();
            var viewModel = CreateViewModel(
                new FakePlaybackCoordinator(),
                new FakeBookPlaybackContentService(null, null),
                autoScrollCoordinator: autoScrollCoordinator);

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.NotifyUserScrollInput();

            Assert.True(viewModel.ShowReturnToCurrentSegment);
            Assert.Equal(PlayerAutoScrollState.ManualBrowsing, viewModel.AutoScrollState);
            viewModel.ReturnToCurrentSegmentCommand.Execute(null);
            Assert.False(viewModel.ShowReturnToCurrentSegment);
            Assert.Equal(PlayerAutoScrollState.AutoCentering, viewModel.AutoScrollState);
        });
    }

    [Fact]
    public async Task SelectSegmentCommand_current_segment_resumes_without_jump()
    {
        var autoScrollCoordinator = new FakePlayerAutoScrollCoordinator();
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
            false));
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
                    ])),
            autoScrollCoordinator: autoScrollCoordinator);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.NotifyUserScrollInput();
        await viewModel.SelectSegmentCommand.ExecuteAsync(viewModel.CurrentSegmentItem);

        Assert.Null(coordinator.LastJumpedSegmentIndex);
        Assert.Equal(PlayerAutoScrollState.AutoCentering, viewModel.AutoScrollState);
        Assert.Equal(1, autoScrollCoordinator.ResumeAutoCenterCallCount);
    }

    [Fact]
    public async Task Segment_navigation_commands_resume_auto_center_after_success()
    {
        var autoScrollCoordinator = new FakePlayerAutoScrollCoordinator();
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
            false));
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
                    ])),
            autoScrollCoordinator: autoScrollCoordinator);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.NotifyUserScrollInput();
        await viewModel.NextSegmentCommand.ExecuteAsync(null);
        Assert.Equal(1, coordinator.NextSegmentCallCount);
        Assert.Equal(PlayerAutoScrollState.AutoCentering, viewModel.AutoScrollState);

        viewModel.NotifyUserScrollInput();
        await viewModel.PreviousSegmentCommand.ExecuteAsync(null);
        Assert.Equal(1, coordinator.PreviousSegmentCallCount);
        Assert.Equal(PlayerAutoScrollState.AutoCentering, viewModel.AutoScrollState);
    }

    [Fact]
    public async Task Chapter_navigation_commands_resume_auto_center_after_success()
    {
        var autoScrollCoordinator = new FakePlayerAutoScrollCoordinator();
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            1,
            "第二章",
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
                PlaybackChapterContent.FromLoaded(1, "第二章", [new SpeechSegment(0, 0, 4, "第二章第一段", "第二章第一段")])),
            autoScrollCoordinator: autoScrollCoordinator);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.NotifyUserScrollInput();
        await viewModel.PreviousChapterCommand.ExecuteAsync(null);
        Assert.Equal(1, coordinator.PreviousChapterCallCount);
        Assert.Equal(PlayerAutoScrollState.AutoCentering, viewModel.AutoScrollState);

        viewModel.NotifyUserScrollInput();
        await viewModel.NextChapterCommand.ExecuteAsync(null);
        Assert.Equal(1, coordinator.NextChapterCallCount);
        Assert.Equal(PlayerAutoScrollState.AutoCentering, viewModel.AutoScrollState);
    }

    [Fact]
    public async Task Playback_snapshot_segment_change_keeps_manual_browsing_state()
    {
        var autoScrollCoordinator = new FakePlayerAutoScrollCoordinator();
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
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
            false));
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
                    ])),
            autoScrollCoordinator: autoScrollCoordinator);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.NotifyUserScrollInput();
        coordinator.Publish(coordinator.CurrentSnapshot with
        {
            SegmentIndex = 1,
            SegmentCount = 3
        });

        Assert.Equal(PlayerAutoScrollState.ManualBrowsing, viewModel.AutoScrollState);
        Assert.False(viewModel.ShouldAutoCenterCurrentSegment);
    }

    [Fact]
    public async Task Loading_states_are_exposed_only_for_inline_loading_indicator()
    {
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(
                PlaybackSnapshot.Idle with
                {
                    State = PlaybackState.Idle
                }),
            new FakeBookPlaybackContentService(null, null));

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.ShowInlineLoadingState);
        Assert.Equal(string.Empty, viewModel.InlineLoadingText);

        viewModel = CreateViewModel(
            new FakePlaybackCoordinator(PlaybackSnapshot.Idle with { State = PlaybackState.Preparing }),
            new FakeBookPlaybackContentService(null, null));
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.True(viewModel.ShowInlineLoadingState);
        Assert.Equal("正在准备", viewModel.InlineLoadingText);

        viewModel = CreateViewModel(
            new FakePlaybackCoordinator(PlaybackSnapshot.Idle with { State = PlaybackState.Buffering }),
            new FakeBookPlaybackContentService(null, null));
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.True(viewModel.ShowInlineLoadingState);
        Assert.Equal("正在加载", viewModel.InlineLoadingText);

        viewModel = CreateViewModel(
            new FakePlaybackCoordinator(PlaybackSnapshot.Idle with { State = PlaybackState.Recovering }),
            new FakeBookPlaybackContentService(null, null));
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.True(viewModel.ShowInlineLoadingState);
        Assert.Equal("正在恢复", viewModel.InlineLoadingText);

        viewModel = CreateViewModel(
            new FakePlaybackCoordinator(PlaybackSnapshot.Idle with { State = PlaybackState.Paused }),
            new FakeBookPlaybackContentService(null, null));
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.False(viewModel.ShowInlineLoadingState);
        Assert.Equal(string.Empty, viewModel.InlineLoadingText);
    }

    private static PlayerViewModel CreateViewModel(
        FakePlaybackCoordinator coordinator,
        IBookPlaybackContentService contentService,
        ITtsRuleQueries? ruleService = null,
        FakeNavigationService? navigationService = null,
        FakeAppFeedbackService? feedbackService = null,
        FakePlayerAutoScrollCoordinator? autoScrollCoordinator = null,
        FakeAppSettingsService? settingsService = null)
    {
        return new PlayerViewModel(
            coordinator,
            contentService,
            ruleService ?? new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
            settingsService ?? new FakeAppSettingsService(AppSettings.Default),
            feedbackService ?? new FakeAppFeedbackService(),
            navigationService ?? new FakeNavigationService(),
            autoScrollCoordinator ?? new FakePlayerAutoScrollCoordinator());
    }

    private sealed class FakePlaybackCoordinator : IPlaybackCoordinator
    {
        public FakePlaybackCoordinator()
            : this(PlaybackSnapshot.Idle)
        {
        }

        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public long? LastChangedRuleId { get; private set; }

        public int? LastChangedSpeakSpeed { get; private set; }

        public int OpenPausedCallCount { get; private set; }

        public OpenBookPlaybackRequest? LastOpenPausedRequest { get; private set; }

        public PlaybackStartRequest? LastStartRequest { get; private set; }

        public int? LastJumpedChapterIndex { get; private set; }

        public int? LastJumpedSegmentChapterIndex { get; private set; }

        public int? LastJumpedSegmentIndex { get; private set; }

        public int RetryCurrentSegmentCallCount { get; private set; }

        public Func<PlayerAutoScrollState>? ReadAutoScrollStateDuringSegmentJump { get; set; }

        public PlayerAutoScrollState? AutoScrollStateObservedDuringLastJumpToSegment { get; private set; }

        public int PreviousSegmentCallCount { get; private set; }

        public int NextSegmentCallCount { get; private set; }

        public int PreviousChapterCallCount { get; private set; }

        public int NextChapterCallCount { get; private set; }

        public List<string> OperationLog { get; } = [];

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken)
        {
            LastStartRequest = request;
            Publish(CurrentSnapshot with
            {
                State = PlaybackState.Playing,
                BookId = request.BookId,
                ChapterIndex = request.ChapterIndex ?? CurrentSnapshot.ChapterIndex,
                SegmentIndex = request.SegmentIndex ?? CurrentSnapshot.SegmentIndex,
                SpeakSpeed = request.SpeakSpeedOverride ?? CurrentSnapshot.SpeakSpeed
            });
            return Task.CompletedTask;
        }

        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken)
        {
            OpenPausedCallCount++;
            LastOpenPausedRequest = request;
            Publish(new PlaybackSnapshot(
                PlaybackState.Paused,
                request.BookId,
                request.BookId == "book-2" ? "另一本书" : "示例小说",
                request.ChapterIndex ?? 0,
                request.BookId == "book-2" ? "第二章" : "第一章",
                request.SegmentIndex ?? 0,
                1,
                1,
                "默认规则",
                request.SpeakSpeedOverride ?? 10,
                0,
                0,
                null,
                false,
                false,
                false,
                request.BookId == "book-2" ? "作者乙" : "作者甲"));
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with { State = PlaybackState.Paused });
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with { State = PlaybackState.Playing });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with { State = PlaybackState.Stopped });
            return Task.CompletedTask;
        }

        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken)
        {
            OperationLog.Add($"JumpToChapter:{chapterIndex}");
            LastJumpedChapterIndex = chapterIndex;
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                ChapterTitle = chapterIndex == 0 ? "第一章" : "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }

        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken)
        {
            OperationLog.Add($"JumpToSegment:{chapterIndex}:{segmentIndex}");
            AutoScrollStateObservedDuringLastJumpToSegment = ReadAutoScrollStateDuringSegmentJump?.Invoke();
            LastJumpedSegmentChapterIndex = chapterIndex;
            LastJumpedSegmentIndex = segmentIndex;
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                SegmentIndex = segmentIndex
            });
            return Task.CompletedTask;
        }

        public Task NextSegmentAsync(CancellationToken cancellationToken)
        {
            OperationLog.Add("NextSegment");
            NextSegmentCallCount++;
            Publish(CurrentSnapshot with
            {
                SegmentIndex = CurrentSnapshot.SegmentIndex + 1,
                SegmentCount = Math.Max(CurrentSnapshot.SegmentCount, CurrentSnapshot.SegmentIndex + 2)
            });
            return Task.CompletedTask;
        }

        public Task PreviousSegmentAsync(CancellationToken cancellationToken)
        {
            OperationLog.Add("PreviousSegment");
            PreviousSegmentCallCount++;
            Publish(CurrentSnapshot with
            {
                SegmentIndex = Math.Max(CurrentSnapshot.SegmentIndex - 1, 0)
            });
            return Task.CompletedTask;
        }

        public Task NextChapterAsync(CancellationToken cancellationToken)
        {
            OperationLog.Add("NextChapter");
            NextChapterCallCount++;
            var nextChapterIndex = CurrentSnapshot.ChapterIndex + 1;
            Publish(CurrentSnapshot with
            {
                ChapterIndex = nextChapterIndex,
                ChapterTitle = nextChapterIndex == 0 ? "第一章" : "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }

        public Task PreviousChapterAsync(CancellationToken cancellationToken)
        {
            OperationLog.Add("PreviousChapter");
            PreviousChapterCallCount++;
            var previousChapterIndex = Math.Max(CurrentSnapshot.ChapterIndex - 1, 0);
            Publish(CurrentSnapshot with
            {
                ChapterIndex = previousChapterIndex,
                ChapterTitle = previousChapterIndex == 0 ? "第一章" : "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken)
        {
            RetryCurrentSegmentCallCount++;
            return Task.CompletedTask;
        }
        public Task SkipCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken)
        {
            LastChangedRuleId = ruleId;
            Publish(CurrentSnapshot with { RuleId = ruleId });
            return Task.CompletedTask;
        }

        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken)
        {
            LastChangedSpeakSpeed = speakSpeed;
            Publish(CurrentSnapshot with { SpeakSpeed = speakSpeed });
            return Task.CompletedTask;
        }

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakeBookPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent? _book;
        private readonly PlaybackChapterContent? _chapter;

        public FakeBookPlaybackContentService(PlaybackBookContent? book, PlaybackChapterContent? chapter)
        {
            _book = book;
            _chapter = chapter;
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            if (_book is null || !string.Equals(_book.BookId, bookId, StringComparison.Ordinal))
            {
                return Task.FromResult<PlaybackBookContent?>(null);
            }

            return Task.FromResult<PlaybackBookContent?>(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            if (_book is null || _chapter is null ||
                !string.Equals(_book.BookId, bookId, StringComparison.Ordinal) ||
                _chapter.ChapterIndex != chapterIndex)
            {
                return Task.FromResult<PlaybackChapterContent?>(null);
            }

            return Task.FromResult<PlaybackChapterContent?>(_chapter);
        }
    }

    private sealed class FakePlayerAutoScrollCoordinator : IPlayerAutoScrollCoordinator
    {
        public PlayerAutoScrollState State { get; private set; } = PlayerAutoScrollState.AutoCentering;

        public bool ShouldAutoCenter => State == PlayerAutoScrollState.AutoCentering;

        public bool ShowReturnToCurrentSegment => State != PlayerAutoScrollState.AutoCentering;

        public int PendingRestoreVersion { get; private set; }

        public int ResumeAutoCenterCallCount { get; private set; }

        public List<string> OperationLog { get; } = [];

        public event EventHandler? StateChanged;

        public void NotifyUserScrollInput()
        {
            OperationLog.Add("NotifyUserScrollInput");
            State = PlayerAutoScrollState.ManualBrowsing;
            PendingRestoreVersion++;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void NotifyPassiveScrollChange()
        {
            OperationLog.Add("NotifyPassiveScrollChange");
            NotifyUserScrollInput();
        }

        public void BeginScrollbarDrag()
        {
            OperationLog.Add("BeginScrollbarDrag");
            State = PlayerAutoScrollState.ScrollbarDragging;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void EndScrollbarDrag()
        {
            OperationLog.Add("EndScrollbarDrag");
            State = PlayerAutoScrollState.ManualBrowsing;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void BeginProgrammaticScroll()
        {
        }

        public void EndProgrammaticScroll()
        {
        }

        public void ResumeAutoCenter()
        {
            OperationLog.Add("ResumeAutoCenter");
            ResumeAutoCenterCallCount++;
            State = PlayerAutoScrollState.AutoCentering;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ResetForPageLeave()
        {
            OperationLog.Add("ResetForPageLeave");
            ResumeAutoCenter();
        }
    }

    private sealed class DelayedBookPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent _book;
        private readonly Queue<Task<PlaybackChapterContent?>> _chapterLoads;
        private int _chapterRequestCount;

        public DelayedBookPlaybackContentService(
            PlaybackBookContent book,
            IEnumerable<Task<PlaybackChapterContent?>> chapterLoads)
        {
            _book = book;
            _chapterLoads = new Queue<Task<PlaybackChapterContent?>>(chapterLoads);
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult<PlaybackBookContent?>(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _chapterRequestCount);
            return _chapterLoads.Count == 0
                ? Task.FromResult<PlaybackChapterContent?>(null)
                : _chapterLoads.Dequeue();
        }

        public async Task WaitForChapterRequestCountAsync(int expectedCount)
        {
            while (Volatile.Read(ref _chapterRequestCount) < expectedCount)
            {
                await Task.Delay(10);
            }
        }
    }

    private sealed class FakeTtsRuleQueries : ITtsRuleQueries
    {
        private readonly IReadOnlyList<TtsRuleSummary> _rules;

        public FakeTtsRuleQueries(IReadOnlyList<TtsRuleSummary> rules)
        {
            _rules = rules;
        }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_rules);
        }

        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public FakeAppSettingsService(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }
        public AppSettings Current => Settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            Settings = (Settings with
            {
                DefaultSpeakSpeed = update.DefaultSpeakSpeed ?? Settings.DefaultSpeakSpeed
            }).Normalize();
            return Task.FromResult(Settings);
        }
    }

    private sealed class FakeAppFeedbackService : IAppFeedbackService
    {
        public string? LastWarningTitle { get; private set; }

        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
            LastWarningTitle = title;
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Cancel);
        }
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public Type? LastNavigationPageType { get; private set; }

        public object? LastNavigationData { get; private set; }

        public INavigationView GetNavigationControl() => throw new NotSupportedException();

        public bool GoBack() => false;

        public bool Navigate(Type pageType) => true;

        public bool Navigate(Type pageType, object? dataContext) => true;

        public bool Navigate(string pageIdOrTargetTag) => true;

        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;

        public bool NavigateWithHierarchy(Type pageType)
        {
            LastNavigationPageType = pageType;
            LastNavigationData = null;
            return true;
        }

        public bool NavigateWithHierarchy(Type pageType, object? dataContext)
        {
            LastNavigationPageType = pageType;
            LastNavigationData = dataContext;
            return true;
        }

        public void SetNavigationControl(INavigationView navigation)
        {
        }
    }

}
