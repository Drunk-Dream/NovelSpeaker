using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Playback;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly IPlaybackDemoRequestFactory _demoRequestFactory;
    private PlaybackRequest? _lastSelectedRequest;

    public PlayerViewModel(
        IPlaybackCoordinator playbackCoordinator,
        IPlaybackDemoRequestFactory demoRequestFactory)
    {
        _playbackCoordinator = playbackCoordinator;
        _demoRequestFactory = demoRequestFactory;
        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
        _playbackCoordinator.SnapshotChanged += OnSnapshotChanged;
    }

    [ObservableProperty]
    private string headline = "使用内置演示音频验证本地播放链路。";

    [ObservableProperty]
    private string currentTitle = "未开始播放";

    [ObservableProperty]
    private string statusText = "准备播放本地音频。";

    [ObservableProperty]
    private string detailText = "可先播放内置 WAV，再切到 MP3 或损坏样本验证错误处理。";

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private string primaryActionText = "播放";

    [ObservableProperty]
    private bool isFaulted;

    [ObservableProperty]
    private PlaybackState currentPlaybackState = PlaybackState.Idle;

    [ObservableProperty]
    private long positionMilliseconds;

    [ObservableProperty]
    private long durationMilliseconds;

    [RelayCommand]
    private async Task PlayDemoWavAsync(CancellationToken cancellationToken)
    {
        _lastSelectedRequest = _demoRequestFactory.CreateWavDemoRequest();
        await _playbackCoordinator.StartAsync(_lastSelectedRequest, cancellationToken);
    }

    [RelayCommand]
    private async Task PlayDemoMp3Async(CancellationToken cancellationToken)
    {
        _lastSelectedRequest = _demoRequestFactory.CreateMp3DemoRequest();
        await _playbackCoordinator.StartAsync(_lastSelectedRequest, cancellationToken);
    }

    [RelayCommand]
    private async Task PlayCorruptDemoAsync(CancellationToken cancellationToken)
    {
        _lastSelectedRequest = _demoRequestFactory.CreateCorruptDemoRequest();
        await _playbackCoordinator.StartAsync(_lastSelectedRequest, cancellationToken);
    }

    [RelayCommand]
    private async Task TogglePlayPauseAsync(CancellationToken cancellationToken)
    {
        if (CurrentPlaybackState == global::NovelSpeaker.Application.Playback.PlaybackState.Playing)
        {
            await _playbackCoordinator.PauseAsync(cancellationToken);
            return;
        }

        if (CurrentPlaybackState == global::NovelSpeaker.Application.Playback.PlaybackState.Paused)
        {
            await _playbackCoordinator.ResumeAsync(cancellationToken);
            return;
        }

        _lastSelectedRequest ??= _demoRequestFactory.CreateWavDemoRequest();
        await _playbackCoordinator.StartAsync(_lastSelectedRequest, cancellationToken);
    }

    [RelayCommand]
    private async Task StopAsync(CancellationToken cancellationToken)
    {
        await _playbackCoordinator.StopAsync(cancellationToken);
    }

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        CurrentPlaybackState = snapshot.State;
        CurrentTitle = string.IsNullOrWhiteSpace(snapshot.DisplayTitle) ? "未开始播放" : snapshot.DisplayTitle;
        PositionMilliseconds = snapshot.PositionMilliseconds;
        DurationMilliseconds = snapshot.DurationMilliseconds;
        IsFaulted = snapshot.State == global::NovelSpeaker.Application.Playback.PlaybackState.Faulted;
        ErrorText = IsFaulted ? snapshot.Message ?? "本地音频播放失败。" : string.Empty;
        StatusText = BuildStatusText(snapshot);
        DetailText = BuildDetailText(snapshot);
        PrimaryActionText = snapshot.State == PlaybackState.Playing ? "暂停" : "播放";
    }

    private static string BuildStatusText(PlaybackSnapshot snapshot)
    {
        return snapshot.State switch
        {
            PlaybackState.Preparing => "正在准备音频",
            PlaybackState.Buffering => "正在缓冲音频",
            PlaybackState.Playing => "正在播放",
            PlaybackState.Paused => "已暂停",
            PlaybackState.Stopped => "已停止",
            PlaybackState.Recovering => "正在恢复",
            PlaybackState.Faulted => "播放失败",
            _ => "待机中"
        };
    }

    private static string BuildDetailText(PlaybackSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Message))
        {
            return snapshot.Message;
        }

        if (snapshot.DurationMilliseconds <= 0)
        {
            return "可先播放内置 WAV，再切到 MP3 或损坏样本验证错误处理。";
        }

        return $"位置 {snapshot.PositionMilliseconds} ms / {snapshot.DurationMilliseconds} ms";
    }
}
