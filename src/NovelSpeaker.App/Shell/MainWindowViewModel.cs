using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Shell;

/// <summary>
/// Projects shell-level navigation state for the main window.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IAppNavigator _navigator;
    private readonly IUiScheduler _uiScheduler;
    private readonly OwnedTaskRegistry _processTasks = new();
    private string? _currentBookId;

    public MainWindowViewModel(
        IPlaybackSnapshotSource playbackCoordinator,
        IAppNavigator navigator,
        IUiScheduler? uiScheduler = null)
    {
        _navigator = navigator;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        ApplySnapshot(playbackCoordinator.CurrentSnapshot);
        playbackCoordinator.SnapshotChanged += OnSnapshotChanged;
    }

    [ObservableProperty]
    private bool isNowPlayingVisible;

    [ObservableProperty]
    private string nowPlayingTitle = string.Empty;

    [ObservableProperty]
    private string nowPlayingStatus = string.Empty;

    [ObservableProperty]
    private NowPlayingVisualState nowPlayingVisualState;

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
