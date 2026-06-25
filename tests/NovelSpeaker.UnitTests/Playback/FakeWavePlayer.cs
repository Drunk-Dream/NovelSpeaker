using NAudio.Wave;

namespace NovelSpeaker.UnitTests.Playback;

internal sealed class FakeWavePlayer : IWavePlayer
{
    private EventHandler<StoppedEventArgs>? _playbackStopped;

    public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;

    public WaveFormat? OutputWaveFormat => WaveProvider?.WaveFormat;

    public float Volume { get; set; } = 1f;

    public IWaveProvider? WaveProvider { get; private set; }

    public event EventHandler<StoppedEventArgs>? PlaybackStopped
    {
        add => _playbackStopped += value;
        remove => _playbackStopped -= value;
    }

    public void Init(IWaveProvider waveProvider)
    {
        WaveProvider = waveProvider;
    }

    public void Play()
    {
        PlaybackState = PlaybackState.Playing;
    }

    public void Pause()
    {
        PlaybackState = PlaybackState.Paused;
    }

    public void Stop()
    {
        PlaybackState = PlaybackState.Stopped;
        _playbackStopped?.Invoke(this, new StoppedEventArgs());
    }

    public void RaisePlaybackStopped(Exception? exception = null)
    {
        PlaybackState = PlaybackState.Stopped;
        _playbackStopped?.Invoke(this, new StoppedEventArgs(exception));
    }

    public EventHandler<StoppedEventArgs>? CapturePlaybackStoppedHandlers()
    {
        return _playbackStopped;
    }

    public void RaiseCapturedPlaybackStopped(EventHandler<StoppedEventArgs>? handlers, Exception? exception = null)
    {
        PlaybackState = PlaybackState.Stopped;
        handlers?.Invoke(this, new StoppedEventArgs(exception));
    }

    public void Dispose()
    {
    }
}
