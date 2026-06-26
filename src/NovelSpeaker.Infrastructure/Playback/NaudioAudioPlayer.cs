using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NovelSpeaker.Application.Playback;
using PlaybackStatus = NovelSpeaker.Application.Playback.PlaybackState;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Uses NAudio to decode and play local audio files.
/// </summary>
public sealed class NaudioAudioPlayer : IAudioPlayer
{
    private static readonly TimeSpan EndOfStreamTolerance = TimeSpan.FromMilliseconds(1);
    private const int PlaybackSampleRate = 44100;
    private const int PlaybackChannels = 2;

    private readonly Func<IWavePlayer> _wavePlayerFactory;
    private readonly SwitchingWaveProvider _switchingWaveProvider = new(new WaveFormat(PlaybackSampleRate, 16, PlaybackChannels));
    private AudioFileReader? _audioFileReader;
    private IWavePlayer? _wavePlayer;
    private EventHandler<StoppedEventArgs>? _playbackStoppedHandler;
    private bool _suppressNextPlaybackStopped;
    private bool _disposed;

    public NaudioAudioPlayer()
        : this(() => new WaveOutEvent())
    {
    }

    public NaudioAudioPlayer(Func<IWavePlayer> wavePlayerFactory)
    {
        _wavePlayerFactory = wavePlayerFactory;
    }

    public PlaybackStatus State { get; private set; } = PlaybackStatus.Idle;

    public TimeSpan Position => GetNormalizedPosition();

    public TimeSpan Duration => _audioFileReader?.TotalTime ?? TimeSpan.Zero;

    public event EventHandler? PlaybackCompleted;
    public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

    public Task LoadAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        try
        {
            ReplaceLoadedAudio(filePath);
            State = PlaybackStatus.Stopped;
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            State = PlaybackStatus.Faulted;
            PlaybackFailed?.Invoke(this, PlaybackErrorMapper.Map(exception));
            throw;
        }
    }

    public void Play()
    {
        ThrowIfDisposed();

        if (_wavePlayer is null || _audioFileReader is null)
        {
            throw new InvalidOperationException("No audio file is currently loaded.");
        }

        try
        {
            _wavePlayer.Play();
            State = PlaybackStatus.Playing;
        }
        catch (Exception exception)
        {
            State = PlaybackStatus.Faulted;
            PlaybackFailed?.Invoke(this, PlaybackErrorMapper.Map(exception));
            throw;
        }
    }

    public void Pause()
    {
        ThrowIfDisposed();

        if (_wavePlayer is null)
        {
            return;
        }

        _wavePlayer.Pause();
        State = PlaybackStatus.Paused;
    }

    public void Stop()
    {
        ThrowIfDisposed();

        if (_wavePlayer is null || _audioFileReader is null)
        {
            State = PlaybackStatus.Stopped;
            return;
        }

        StopWavePlayerIfActive();
        _audioFileReader.CurrentTime = TimeSpan.Zero;
        State = PlaybackStatus.Stopped;
    }

    public void Seek(TimeSpan position)
    {
        ThrowIfDisposed();

        if (_audioFileReader is null)
        {
            return;
        }

        var clamped = position;
        if (clamped < TimeSpan.Zero)
        {
            clamped = TimeSpan.Zero;
        }
        else if (clamped > _audioFileReader.TotalTime)
        {
            clamped = _audioFileReader.TotalTime;
        }

        _audioFileReader.CurrentTime = clamped;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        if (_wavePlayer is not null)
        {
            if (_playbackStoppedHandler is not null)
            {
                _wavePlayer.PlaybackStopped -= _playbackStoppedHandler;
                _playbackStoppedHandler = null;
            }

            _wavePlayer.Dispose();
            _wavePlayer = null;
        }

        _switchingWaveProvider.SetSource(null);
        _audioFileReader?.Dispose();
        _audioFileReader = null;

        State = PlaybackStatus.Stopped;
        return ValueTask.CompletedTask;
    }

    private void ReplaceLoadedAudio(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Audio file was not found.", filePath);
        }

        EnsureWavePlayerInitialized();

        var reader = new AudioFileReader(filePath);
        var playbackProvider = CreatePlaybackProvider(reader);
        var previousReader = _audioFileReader;

        StopWavePlayerIfActive();

        _switchingWaveProvider.SetSource(playbackProvider);
        _audioFileReader = reader;
        previousReader?.Dispose();
    }

    private void EnsureWavePlayerInitialized()
    {
        if (_wavePlayer is not null)
        {
            return;
        }

        _wavePlayer = _wavePlayerFactory();
        _wavePlayer.Init(_switchingWaveProvider);
        _playbackStoppedHandler = (_, e) => OnPlaybackStopped(e);
        _wavePlayer.PlaybackStopped += _playbackStoppedHandler;
    }

    private static IWaveProvider CreatePlaybackProvider(AudioFileReader reader)
    {
        ISampleProvider sampleProvider = reader;
        sampleProvider = sampleProvider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(sampleProvider),
            2 => sampleProvider,
            _ => throw new InvalidOperationException($"Unsupported audio channel count: {sampleProvider.WaveFormat.Channels}.")
        };

        if (sampleProvider.WaveFormat.SampleRate != PlaybackSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, PlaybackSampleRate);
        }

        return sampleProvider.ToWaveProvider16();
    }

    private void OnPlaybackStopped(StoppedEventArgs e)
    {
        if (_suppressNextPlaybackStopped)
        {
            _suppressNextPlaybackStopped = false;
            return;
        }

        if (e.Exception is not null)
        {
            State = PlaybackStatus.Faulted;
            PlaybackFailed?.Invoke(this, PlaybackErrorMapper.Map(e.Exception));
            return;
        }

        if (_audioFileReader is null)
        {
            State = PlaybackStatus.Stopped;
            return;
        }

        // WaveOutEvent may report the stop callback slightly before CurrentTime reaches TotalTime,
        // especially for compressed formats like MP3. Once a non-suppressed stop arrives without
        // an exception, treat it as natural completion so higher layers can advance playback.
        State = PlaybackStatus.Stopped;
        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
    }

    private TimeSpan GetNormalizedPosition()
    {
        if (_audioFileReader is null)
        {
            return TimeSpan.Zero;
        }

        return IsAtEnd() ? _audioFileReader.TotalTime : _audioFileReader.CurrentTime;
    }

    private bool IsAtEnd()
    {
        if (_audioFileReader is null)
        {
            return false;
        }

        var remaining = _audioFileReader.TotalTime - _audioFileReader.CurrentTime;
        return remaining <= EndOfStreamTolerance;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void StopWavePlayerIfActive()
    {
        if (_wavePlayer is null || _wavePlayer.PlaybackState == NAudio.Wave.PlaybackState.Stopped)
        {
            return;
        }

        _suppressNextPlaybackStopped = true;
        _wavePlayer.Stop();
    }

    private sealed class SwitchingWaveProvider : IWaveProvider
    {
        private readonly object _gate = new();
        private readonly WaveFormat _waveFormat;
        private IWaveProvider? _source;

        public SwitchingWaveProvider(WaveFormat waveFormat)
        {
            _waveFormat = waveFormat;
        }

        public WaveFormat WaveFormat => _waveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                return _source?.Read(buffer, offset, count) ?? 0;
            }
        }

        public void SetSource(IWaveProvider? source)
        {
            lock (_gate)
            {
                _source = source;
            }
        }
    }
}
