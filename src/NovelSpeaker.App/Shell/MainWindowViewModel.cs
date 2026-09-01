using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Shell;

/// <summary>
/// Projects shell-level navigation state for the main window.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IAppNavigator _navigator;
    private readonly IUiScheduler _uiScheduler;
    private readonly IThemeToggleService _themeToggleService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly OwnedTaskRegistry _processTasks = new();
    private string? _currentBookId;

    public MainWindowViewModel(
        IPlaybackSnapshotSource playbackCoordinator,
        ShellActiveCacheController activeCache,
        ShellChapterExportController chapterExport,
        IAppNavigator navigator,
        IThemeToggleService themeToggleService,
        IAppFeedbackService feedbackService,
        IUiScheduler? uiScheduler = null)
    {
        ActiveCache = activeCache;
        ChapterExport = chapterExport;
        _navigator = navigator;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _themeToggleService = themeToggleService ?? throw new ArgumentNullException(nameof(themeToggleService));
        _feedbackService = feedbackService ?? throw new ArgumentNullException(nameof(feedbackService));
        ToggleLightDarkThemeCommand = new AsyncRelayCommand(
            ToggleLightDarkThemeAsync,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        ApplySnapshot(playbackCoordinator.CurrentSnapshot);
        RefreshThemeToggleProjection();
        playbackCoordinator.SnapshotChanged += OnSnapshotChanged;
        _themeToggleService.EffectiveThemeChanged += OnEffectiveThemeChanged;
    }

    public ShellActiveCacheController ActiveCache { get; }

    public ShellChapterExportController ChapterExport { get; }

    public IAsyncRelayCommand ToggleLightDarkThemeCommand { get; }

    [ObservableProperty]
    private bool isNowPlayingVisible;

    [ObservableProperty]
    private string nowPlayingTitle = string.Empty;

    [ObservableProperty]
    private string nowPlayingStatus = string.Empty;

    [ObservableProperty]
    private NowPlayingVisualState nowPlayingVisualState;

    [ObservableProperty]
    private string themeToggleText = "切换到深色模式";

    [ObservableProperty]
    private ThemeToggleVisualState themeToggleVisualState = ThemeToggleVisualState.SwitchToDark;

    [RelayCommand]
    private async Task NavigateToNowPlayingAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentBookId))
        {
            return;
        }

        await _navigator.NavigateAsync(
            new PlayerRoute(_currentBookId, PlayerNavigationMode.ReturnToCurrentSession),
            cancellationToken).ConfigureAwait(true);
    }

    private async Task ToggleLightDarkThemeAsync()
    {
        var cancellationToken = _lifetimeCancellation.Token;
        var result = await _themeToggleService.ToggleLightDarkAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsStale || result.IsSuccess)
        {
            return;
        }

        var exception = result.Exception ?? new InvalidOperationException("主题切换失败。");
        await _uiScheduler.InvokeAsync(
            () => _feedbackService.ShowProjectedNotification(
                "主题切换失败",
                _feedbackService.Project(exception)),
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _themeToggleService.EffectiveThemeChanged -= OnEffectiveThemeChanged;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private void OnEffectiveThemeChanged(object? sender, EventArgs e)
    {
        if (!_uiScheduler.CheckAccess())
        {
            _processTasks.Register(_uiScheduler.InvokeAsync(RefreshThemeToggleProjection));
            return;
        }

        RefreshThemeToggleProjection();
    }

    private void RefreshThemeToggleProjection()
    {
        var switchToDark = _themeToggleService.EffectiveTheme != AppTheme.Dark;
        ThemeToggleText = switchToDark ? "切换到深色模式" : "切换到浅色模式";
        ThemeToggleVisualState = switchToDark
            ? ThemeToggleVisualState.SwitchToDark
            : ThemeToggleVisualState.SwitchToLight;
    }

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        if (!_uiScheduler.CheckAccess())
        {
            _processTasks.Register(_uiScheduler.InvokeAsync(() => ApplySnapshot(snapshot)));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        _currentBookId = snapshot.BookId;
        IsNowPlayingVisible = !string.IsNullOrWhiteSpace(snapshot.BookId) && snapshot.State != PlaybackState.Idle;
        NowPlayingTitle = snapshot.BookTitle ?? string.Empty;
        NowPlayingStatus = BuildStatus(snapshot);
        NowPlayingVisualState = snapshot.State switch
        {
            PlaybackState.Playing => NowPlayingVisualState.Playing,
            PlaybackState.Paused => NowPlayingVisualState.Paused,
            PlaybackState.Faulted => NowPlayingVisualState.Faulted,
            _ => NowPlayingVisualState.Inactive
        };
    }

    private static string BuildStatus(PlaybackSnapshot snapshot)
    {
        return snapshot.State switch
        {
            PlaybackState.Playing => "正在播放",
            PlaybackState.Paused => "已暂停",
            PlaybackState.Stopped => "已停止",
            PlaybackState.Faulted => "播放出错",
            PlaybackState.Buffering or PlaybackState.Preparing or PlaybackState.Recovering => "正在准备",
            _ => string.IsNullOrWhiteSpace(snapshot.Message) ? string.Empty : snapshot.Message
        };
    }
}
