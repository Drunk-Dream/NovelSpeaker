using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.TestKit.Common;
using NovelSpeaker.TestKit.Navigation;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels.Player;

public sealed partial class PlayerViewModelTests
{
    [Fact]
    public void Preparation_feedback_waits_before_showing_and_disappears_when_audio_starts()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new FakePlaybackCoordinator(CreatePreparationSnapshot(PlaybackState.Preparing));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(null, null),
            timeProvider: timeProvider);

        try
        {
            Assert.False(viewModel.ShowInlineLoadingState);
            Assert.Equal("正在准备音频", viewModel.InlineLoadingText);
            Assert.Equal("暂停", viewModel.PrimaryActionText);

            timeProvider.Advance(TimeSpan.FromMilliseconds(200));
            Assert.False(viewModel.ShowInlineLoadingState);

            timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            Assert.True(viewModel.ShowInlineLoadingState);

            coordinator.Publish(coordinator.CurrentSnapshot with { State = PlaybackState.Playing });

            Assert.False(viewModel.ShowInlineLoadingState);
        }
        finally
        {
            viewModel.OnPageNavigatedFrom();
        }
    }

    [Fact]
    public void Preparation_feedback_restarts_for_a_new_target_and_is_page_activation_owned()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new FakePlaybackCoordinator(CreatePreparationSnapshot(PlaybackState.Preparing));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(null, null),
            timeProvider: timeProvider);

        try
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(200));
            coordinator.Publish(coordinator.CurrentSnapshot with
            {
                TargetRevision = 2,
                SegmentIndex = 1
            });

            timeProvider.Advance(TimeSpan.FromMilliseconds(200));
            Assert.False(viewModel.ShowInlineLoadingState);
            timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            Assert.True(viewModel.ShowInlineLoadingState);

            viewModel.OnPageNavigatedFrom();
            Assert.False(viewModel.ShowInlineLoadingState);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            Assert.False(viewModel.ShowInlineLoadingState);

            viewModel.OnPageNavigatedTo(new PageActivationController().Activate());
            timeProvider.Advance(TimeSpan.FromMilliseconds(200));
            Assert.False(viewModel.ShowInlineLoadingState);
            timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            Assert.True(viewModel.ShowInlineLoadingState);
        }
        finally
        {
            viewModel.OnPageNavigatedFrom();
        }
    }

    [Fact]
    public async Task Recovery_feedback_uses_regeneration_semantics_and_pause_command_cancels_play_intent()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new FakePlaybackCoordinator(CreatePreparationSnapshot(PlaybackState.Recovering));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(null, null),
            timeProvider: timeProvider);

        try
        {
            await viewModel.LoadAsync(CancellationToken.None);
            Assert.True(viewModel.CanTogglePlayPause);
            Assert.Equal("正在重新生成音频", viewModel.InlineLoadingText);
            Assert.Equal("暂停", viewModel.PrimaryActionText);
            timeProvider.Advance(TimeSpan.FromMilliseconds(300));
            Assert.True(viewModel.ShowInlineLoadingState);

            await viewModel.TogglePlayPauseCommand.ExecuteAsync(null);

            Assert.Equal(1, coordinator.PauseCallCount);
            Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
            Assert.False(viewModel.ShowInlineLoadingState);
        }
        finally
        {
            viewModel.OnPageNavigatedFrom();
        }
    }

    private static PlaybackSnapshot CreatePreparationSnapshot(PlaybackState state) => new(
        state,
        "book-1",
        "示例小说",
        0,
        "第一章",
        0,
        2,
        TestSpeechProviders.Id(1),
        "默认规则",
        10,
        0,
        0,
        null,
        false,
        false)
    {
        TargetRevision = 1
    };
}
