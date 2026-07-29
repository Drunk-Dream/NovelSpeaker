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

public sealed partial class PlayerViewModelTests
{
    [Fact]
    public async Task Volume_projection_and_changes_use_the_shared_playback_session()
    {
        var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Paused,
            BookId = "book-1",
            BookTitle = "示例小说",
            ChapterTitle = "第一章",
            SegmentCount = 1,
            Volume = 0.4
        });
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(null, null));

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(0.4, viewModel.Volume);
        Assert.Equal("40%", viewModel.VolumePercentText);

        viewModel.Volume = 0.2;

        Assert.Equal(0.2, coordinator.LastVolume);

        viewModel.ToggleVolumeMenuCommand.Execute(null);
        Assert.True(viewModel.IsVolumeMenuOpen);
        viewModel.ToggleVolumeMenuCommand.Execute(null);
        Assert.False(viewModel.IsVolumeMenuOpen);
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
}
