namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Loads and controls one local audio file at a time.
/// </summary>
public interface IAudioPlayer : IAsyncDisposable
{
    PlaybackState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    event EventHandler? PlaybackCompleted;
    event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

    Task LoadAsync(string filePath, CancellationToken cancellationToken);

    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
}
