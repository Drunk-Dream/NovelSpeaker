using NAudio.Wave;

namespace NovelSpeaker.UnitTests.Playback;

internal sealed class FakeWavePlayer : IWavePlayer
{
    public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;

    public WaveFormat? OutputWaveFormat => WaveProvider?.WaveFormat;

    public float Volume { get; set; } = 1f;

    public IWaveProvider? WaveProvider { get; private set; }

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

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
        PlaybackStopped?.Invoke(this, new StoppedEventArgs());
    }

    public void RaisePlaybackStopped(Exception? exception = null)
    {
        PlaybackState = PlaybackState.Stopped;
        PlaybackStopped?.Invoke(this, new StoppedEventArgs(exception));
    }

    public void Dispose()
    {
    }
}
