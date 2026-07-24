using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Navigation;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Projects shell-level navigation state for the main window.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IAppNavigator _navigator;
    private string? _currentBookId;

    public MainWindowViewModel(
        IPlaybackSnapshotSource playbackCoordinator,
        IAppNavigator navigator)
    {
        _navigator = navigator;
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
    private SymbolRegular nowPlayingSymbol = SymbolRegular.Headphones24;

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
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
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
        NowPlayingSymbol = snapshot.State switch
        {
            PlaybackState.Playing => SymbolRegular.PlayCircle24,
            PlaybackState.Paused => SymbolRegular.PauseCircle24,
            PlaybackState.Faulted => SymbolRegular.ErrorCircle24,
            _ => SymbolRegular.Headphones24
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
