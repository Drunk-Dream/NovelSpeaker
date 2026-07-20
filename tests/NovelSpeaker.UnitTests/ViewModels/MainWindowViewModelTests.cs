using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Idle_snapshot_hides_now_playing_entry()
    {
        WpfTestHost.RunInSta(() =>
        {
            var navigationService = new FakeNavigationService();
            var viewModel = new MainWindowViewModel(new FakePlaybackCoordinator(PlaybackSnapshot.Idle), navigationService);

            Assert.False(viewModel.IsNowPlayingVisible);
            Assert.Equal(SymbolRegular.Headphones24, viewModel.NowPlayingSymbol);
        });
    }

    [Theory]
    [InlineData(PlaybackState.Playing, "正在播放", "示例小说", SymbolRegular.PlayCircle24)]
    [InlineData(PlaybackState.Paused, "已暂停", "示例小说", SymbolRegular.PauseCircle24)]
    [InlineData(PlaybackState.Stopped, "已停止", "示例小说", SymbolRegular.Headphones24)]
    [InlineData(PlaybackState.Faulted, "播放出错", "示例小说", SymbolRegular.ErrorCircle24)]
    public void Snapshot_projection_updates_now_playing_entry(
        PlaybackState state,
        string status,
        string title,
        SymbolRegular symbol)
    {
        WpfTestHost.RunInSta(() =>
        {
            var navigationService = new FakeNavigationService();
            var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle);
            var viewModel = new MainWindowViewModel(coordinator, navigationService);

            coordinator.Publish(new PlaybackSnapshot(
                state,
                "book-1",
                title,
                0,
                "第一章",
                0,
                3,
                1,
                "默认规则",
                10,
                0,
                1000,
                "message",
                false,
                false));

            Assert.True(viewModel.IsNowPlayingVisible);
            Assert.Equal(status, viewModel.NowPlayingStatus);
            Assert.Equal(title, viewModel.NowPlayingTitle);
            Assert.Equal(symbol, viewModel.NowPlayingSymbol);
        });
    }

    [Fact]
    public async Task NavigateToNowPlayingCommand_uses_player_request_without_playback_control()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var navigationService = new FakeNavigationService();
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Paused,
                "book-9",
                "示例小说",
                0,
                "第一章",
                0,
                3,
                1,
                "默认规则",
                10,
                0,
                1000,
                null,
                false,
                false));
            var viewModel = new MainWindowViewModel(coordinator, navigationService);

            await viewModel.NavigateToNowPlayingCommand.ExecuteAsync(null);

            Assert.Equal(typeof(PlayerPage), navigationService.LastNavigationPageType);
            var request = Assert.IsType<PlayerNavigationRequest>(navigationService.LastNavigationData);
            Assert.Equal("book-9", request.BookId);
            Assert.Equal(PlayerNavigationMode.ReturnToCurrentSession, request.Mode);
        });
    }

    [Fact]
    public void Missing_rule_snapshot_still_shows_now_playing_entry_until_context_is_cleared()
    {
        WpfTestHost.RunInSta(() =>
        {
            var navigationService = new FakeNavigationService();
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
                "作者甲",
                false));
            var viewModel = new MainWindowViewModel(coordinator, navigationService);

            Assert.True(viewModel.IsNowPlayingVisible);
            Assert.Equal("示例小说", viewModel.NowPlayingTitle);
            Assert.Equal("已停止", viewModel.NowPlayingStatus);
            Assert.Equal(SymbolRegular.Headphones24, viewModel.NowPlayingSymbol);
        });
    }

    private sealed class FakePlaybackCoordinator : IPlaybackSnapshotSource
    {
        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;


        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeNavigationService : IGuardedNavigationService
    {
        public Type? LastNavigationPageType { get; private set; }

        public object? LastNavigationData { get; private set; }

        public bool IsBypassingGuard => false;

        public Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false)
        {
            return Task.FromResult(false);
        }

        public Task<bool> NavigateAsync(string pageIdOrTargetTag, CancellationToken cancellationToken, bool bypassGuard = false)
        {
            return Task.FromResult(true);
        }

        public Task<bool> NavigateWithHierarchyAsync(Type pageType, object? dataContext, CancellationToken cancellationToken, bool bypassGuard = false)
        {
            LastNavigationPageType = pageType;
            LastNavigationData = dataContext;
            return Task.FromResult(true);
        }
    }
}
