using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.App.PresentationTests.TestDoubles;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Shell.Navigation;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Main_window_active_cache_contracts_preserve_process_scope_and_now_playing_projection()
    {
        Active_cache_projection_is_process_scoped_and_unchanged_by_playback_updates();
        Idle_snapshot_hides_now_playing_entry();
        Snapshot_projection_updates_now_playing_entry();
        Missing_rule_snapshot_still_shows_now_playing_entry_until_context_is_cleared();
    }

    [Fact]
    public async Task Main_window_navigation_contracts_use_the_current_playback_session()
    {
        await NavigateToNowPlayingCommand_uses_player_request_without_playback_control();
        await NavigateToNowPlayingCommand_captures_the_current_route_and_does_not_nest_player_routes();
    }

    [Fact]
    public async Task Theme_toggle_projection_tracks_effective_theme_and_executes_shell_toggle()
    {
        var themeService = new FakeThemeToggleService(AppTheme.Light);
        var viewModel = new MainWindowViewModel(
            new FakePlaybackCoordinator(PlaybackSnapshot.Idle),
            new ShellActiveCacheController(
                new FakeActiveCacheCoordinator(),
                new FakeAppFeedbackService()),
            CreateChapterExportProjection(),
            new FakeNavigationService(),
            themeToggleService: themeService,
            feedbackService: new FakeAppFeedbackService());

        Assert.Equal("切换到深色模式", viewModel.ThemeToggleText);
        Assert.Equal(ThemeToggleVisualState.SwitchToDark, viewModel.ThemeToggleVisualState);

        themeService.Publish(AppTheme.Dark);

        Assert.Equal("切换到浅色模式", viewModel.ThemeToggleText);
        Assert.Equal(ThemeToggleVisualState.SwitchToLight, viewModel.ThemeToggleVisualState);

        await viewModel.ToggleLightDarkThemeCommand.ExecuteAsync(null);

        Assert.Equal(1, themeService.ToggleCount);
        Assert.Equal(AppTheme.Light, themeService.EffectiveTheme);
        Assert.Equal("切换到深色模式", viewModel.ThemeToggleText);
    }

    [Fact]
    public async Task Theme_toggle_command_accepts_concurrent_clicks_while_first_operation_is_pending()
    {
        var themeService = new GatedThemeToggleService();
        var viewModel = new MainWindowViewModel(
            new FakePlaybackCoordinator(PlaybackSnapshot.Idle),
            new ShellActiveCacheController(
                new FakeActiveCacheCoordinator(),
                new FakeAppFeedbackService()),
            CreateChapterExportProjection(),
            new FakeNavigationService(),
            themeService,
            new FakeAppFeedbackService());

        var firstToggle = viewModel.ToggleLightDarkThemeCommand.ExecuteAsync(null);
        await themeService.FirstStarted;

        var secondToggle = viewModel.ToggleLightDarkThemeCommand.ExecuteAsync(null);
        await themeService.SecondStarted;

        Assert.True(viewModel.ToggleLightDarkThemeCommand.CanExecute(null));

        themeService.CompleteFirst();
        await Task.WhenAll(firstToggle, secondToggle);

        Assert.Equal(2, themeService.ToggleCount);
        Assert.Equal(AppTheme.Light, themeService.EffectiveTheme);
    }

    private void Active_cache_projection_is_process_scoped_and_unchanged_by_playback_updates()
    {
        var playback = new FakePlaybackCoordinator(PlaybackSnapshot.Idle);
        var activeCache = new FakeActiveCacheCoordinator(new ActiveCacheSnapshot(
            Guid.NewGuid(),
            "book-1",
            "示例小说",
            ActiveCacheBatchStatus.Running,
            1,
            2,
            3,
            6,
            1,
            "第二章",
            [
                new ActiveCacheChapterSnapshot(
                        0,
                        "第一章",
                        3,
                        3,
                        ActiveCacheChapterStatus.Completed,
                        null),
                    new ActiveCacheChapterSnapshot(
                        1,
                        "第二章",
                        0,
                        3,
                        ActiveCacheChapterStatus.Running,
                        null)
            ],
            null));
        var activeProjection = new ShellActiveCacheController(
            activeCache,
            new FakeAppFeedbackService());
        var viewModel = new MainWindowViewModel(
            playback,
            activeProjection,
            CreateChapterExportProjection(),
            new FakeNavigationService(),
            new FakeThemeToggleService(AppTheme.Light),
            new FakeAppFeedbackService());

        playback.Publish(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-2",
            "另一本书",
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

        Assert.Same(activeProjection, viewModel.ActiveCache);
        Assert.True(viewModel.ActiveCache.IsVisible);
        Assert.Equal("缓存中 · 1/2 章 · 50%", viewModel.ActiveCache.CompactStatusText);
    }

    private void Idle_snapshot_hides_now_playing_entry()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(PlaybackSnapshot.Idle),
            navigationService);

        Assert.False(viewModel.IsNowPlayingVisible);
        Assert.Equal(NowPlayingVisualState.Inactive, viewModel.NowPlayingVisualState);
    }

    private void Snapshot_projection_updates_now_playing_entry()
    {
        foreach (var (state, status, title, visualState) in new[]
                 {
                     (PlaybackState.Playing, "正在播放", "示例小说", NowPlayingVisualState.Playing),
                     (PlaybackState.Paused, "已暂停", "示例小说", NowPlayingVisualState.Paused),
                     (PlaybackState.Stopped, "已停止", "示例小说", NowPlayingVisualState.Inactive),
                     (PlaybackState.Faulted, "播放出错", "示例小说", NowPlayingVisualState.Faulted)
                 })
        {
            var navigationService = new FakeNavigationService();
            var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle);
            var viewModel = CreateViewModel(coordinator, navigationService);

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
            Assert.Equal(visualState, viewModel.NowPlayingVisualState);
        }
    }

    private async Task NavigateToNowPlayingCommand_uses_player_request_without_playback_control()
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
        var viewModel = CreateViewModel(coordinator, navigationService);

        await viewModel.NavigateToNowPlayingCommand.ExecuteAsync(null);

        var request = Assert.IsType<PlayerNavigationRequest>(navigationService.LastNavigationRoute);
        Assert.Equal("book-9", request.BookId);
        Assert.Same(AppRoutes.Library, request.ReturnRoute);
        Assert.Equal(PlayerNavigationMode.ReturnToCurrentSession, request.Mode);
    }

    private async Task NavigateToNowPlayingCommand_captures_the_current_route_and_does_not_nest_player_routes()
    {
        var navigationService = new FakeNavigationService
        {
            CurrentRoute = new BookDetailsRoute("book-9")
        };
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-9",
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
        var viewModel = CreateViewModel(coordinator, navigationService);

        await viewModel.NavigateToNowPlayingCommand.ExecuteAsync(null);

        var request = Assert.IsType<PlayerNavigationRequest>(navigationService.LastNavigationRoute);
        Assert.Equal(new BookDetailsRoute("book-9"), request.ReturnRoute);

        navigationService.LastNavigationRoute = null;
        navigationService.CurrentRoute = request;
        await viewModel.NavigateToNowPlayingCommand.ExecuteAsync(null);

        Assert.Null(navigationService.LastNavigationRoute);
    }

    private void Missing_rule_snapshot_still_shows_now_playing_entry_until_context_is_cleared()
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
        var viewModel = CreateViewModel(coordinator, navigationService);

        Assert.True(viewModel.IsNowPlayingVisible);
        Assert.Equal("示例小说", viewModel.NowPlayingTitle);
        Assert.Equal("已停止", viewModel.NowPlayingStatus);
        Assert.Equal(NowPlayingVisualState.Inactive, viewModel.NowPlayingVisualState);
    }

    private static MainWindowViewModel CreateViewModel(
        IPlaybackSnapshotSource playbackCoordinator,
        IAppNavigator navigator) =>
        new(
            playbackCoordinator,
            new ShellActiveCacheController(
                new FakeActiveCacheCoordinator(),
                new FakeAppFeedbackService()),
            CreateChapterExportProjection(),
            navigator,
            new FakeThemeToggleService(AppTheme.Light),
            new FakeAppFeedbackService());

    private static ShellChapterExportController CreateChapterExportProjection() =>
        new(
            new FakeChapterExportCoordinator(),
            new FakeAppFeedbackService(),
            new FakePresentationLauncher());

    private sealed class FakeAppFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception) =>
            new("操作失败。", UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakePresentationLauncher : IPresentationLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeThemeToggleService(AppTheme effectiveTheme) : IThemeToggleService
    {
        public AppTheme EffectiveTheme { get; private set; } = effectiveTheme;

        public int ToggleCount { get; private set; }

        public event EventHandler? EffectiveThemeChanged;

        public Task<ThemePreferenceChangeResult> ToggleLightDarkAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ToggleCount++;
            EffectiveTheme = EffectiveTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(new ThemePreferenceChangeResult(
                true,
                false,
                EffectiveTheme == AppTheme.Dark ? "Dark" : "Light"));
        }

        public void Publish(AppTheme theme)
        {
            EffectiveTheme = theme;
            EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class GatedThemeToggleService : IThemeToggleService
    {
        private readonly SemaphoreSlim _toggleLock = new(1, 1);
        private int _toggleCount;
        private readonly TaskCompletionSource _firstStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public AppTheme EffectiveTheme { get; private set; } = AppTheme.Light;

        public Task FirstStarted => _firstStarted.Task;

        public Task SecondStarted => _secondStarted.Task;

        public int ToggleCount => Volatile.Read(ref _toggleCount);

        public event EventHandler? EffectiveThemeChanged;

        public async Task<ThemePreferenceChangeResult> ToggleLightDarkAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var invocation = Interlocked.Increment(ref _toggleCount);
            if (invocation == 1)
            {
                _firstStarted.TrySetResult();
            }
            else
            {
                _secondStarted.TrySetResult();
            }

            await _toggleLock.WaitAsync(cancellationToken);
            try
            {
                if (invocation == 1)
                {
                    await _firstCompletion.Task.WaitAsync(cancellationToken);
                }

                EffectiveTheme = EffectiveTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
                EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
                return new ThemePreferenceChangeResult(
                    true,
                    false,
                    EffectiveTheme == AppTheme.Dark ? "Dark" : "Light");
            }
            finally
            {
                _toggleLock.Release();
            }
        }

        public void CompleteFirst()
        {
            _firstCompletion.TrySetResult();
        }
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

    private sealed class FakeNavigationService : IAppNavigator
    {
        public AppRoute? LastNavigationRoute { get; set; }

        public AppRoute CurrentRoute { get; set; } = AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false)
        {
            return Task.FromResult(false);
        }

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false)
        {
            LastNavigationRoute = route;
            CurrentRoute = route;
            return Task.FromResult(true);
        }
    }
}
