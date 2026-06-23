using NAudio.Wave;
using NovelSpeaker.Application.Playback;
using PlaybackStatus = NovelSpeaker.Application.Playback.PlaybackState;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Uses NAudio to decode and play local audio files.
/// </summary>
public sealed class NaudioAudioPlayer : IAudioPlayer
{
    private static readonly TimeSpan EndOfStreamTolerance = TimeSpan.FromMilliseconds(1);
    private readonly Func<IWavePlayer> _wavePlayerFactory;
    private AudioFileReader? _audioFileReader;
    private IWavePlayer? _wavePlayer;
    private bool _suppressStoppedEvent;
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

        _suppressStoppedEvent = true;
        _wavePlayer.Stop();
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
            _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }

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

        if (_wavePlayer is not null)
        {
            _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }

        _audioFileReader?.Dispose();
        _audioFileReader = new AudioFileReader(filePath);
        _suppressStoppedEvent = false;
        _wavePlayer = _wavePlayerFactory();
        _wavePlayer.Init(_audioFileReader);
        _wavePlayer.PlaybackStopped += OnPlaybackStopped;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_suppressStoppedEvent)
        {
            _suppressStoppedEvent = false;
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

        if (IsAtEnd())
        {
            State = PlaybackStatus.Stopped;
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }
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
}
