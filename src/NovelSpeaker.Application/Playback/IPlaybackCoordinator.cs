namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Coordinates book-oriented playback sessions, navigation, and UI snapshots.
/// </summary>
public interface IPlaybackCoordinator : IAsyncDisposable
{
    PlaybackSnapshot CurrentSnapshot { get; }

    event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task NextSegmentAsync(CancellationToken cancellationToken);
    Task PreviousSegmentAsync(CancellationToken cancellationToken);
    Task NextChapterAsync(CancellationToken cancellationToken);
    Task PreviousChapterAsync(CancellationToken cancellationToken);
    Task RetryCurrentSegmentAsync(CancellationToken cancellationToken);
    Task SkipCurrentSegmentAsync(CancellationToken cancellationToken);
    Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken);
    Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken);
}
