using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.UnitTests.Playback;

internal sealed class FakeAudioPlayer : IAudioPlayer
{
    private readonly List<EventHandler> _completedSubscribers = [];
    private readonly List<EventHandler<PlaybackErrorEventArgs>> _failedSubscribers = [];
    private readonly List<EventHandler> _completedHistory = [];
    private readonly List<EventHandler<PlaybackErrorEventArgs>> _failedHistory = [];
    private Exception? _loadException;
    private PlaybackErrorEventArgs? _loadError;

    public PlaybackState State { get; private set; } = PlaybackState.Idle;

    public TimeSpan Position { get; private set; } = TimeSpan.Zero;

    public TimeSpan Duration { get; private set; } = TimeSpan.FromSeconds(2);

    public string? LoadedFilePath { get; private set; }

    public int CompletedSubscriptionCount => _completedHistory.Count;

    public int FailedSubscriptionCount => _failedHistory.Count;

    public event EventHandler? PlaybackCompleted
    {
        add
        {
            if (value is null)
            {
                return;
            }

            _completedSubscribers.Add(value);
            _completedHistory.Add(value);
        }
        remove
        {
            if (value is null)
            {
                return;
            }

            _completedSubscribers.Remove(value);
        }
    }

    public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed
    {
        add
        {
            if (value is null)
            {
                return;
            }

            _failedSubscribers.Add(value);
            _failedHistory.Add(value);
        }
        remove
        {
            if (value is null)
            {
                return;
            }

            _failedSubscribers.Remove(value);
        }
    }

    public void ConfigureLoadFailure(Exception exception, PlaybackErrorEventArgs error)
    {
        _loadException = exception;
        _loadError = error;
    }

    public void SetDuration(TimeSpan duration)
    {
        Duration = duration;
    }

    public void SetPosition(TimeSpan position)
    {
        Position = position;
    }

    public Task LoadAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_loadException is not null && _loadError is not null)
        {
            State = PlaybackState.Faulted;
            RaiseCurrentFailed(_loadError);
            throw _loadException;
        }

        LoadedFilePath = filePath;
        Position = TimeSpan.Zero;
        State = PlaybackState.Stopped;
        return Task.CompletedTask;
    }

    public void Play()
    {
        State = PlaybackState.Playing;
    }

    public void Pause()
    {
        State = PlaybackState.Paused;
    }

    public void Stop()
    {
        Position = TimeSpan.Zero;
        State = PlaybackState.Stopped;
    }

    public void Seek(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            Position = TimeSpan.Zero;
            return;
        }

        Position = position > Duration ? Duration : position;
    }

    public void RaiseCompleted()
    {
        foreach (var subscriber in _completedSubscribers.ToArray())
        {
            subscriber(this, EventArgs.Empty);
        }
    }

    public void RaiseHistoricalCompleted(int subscriptionIndex)
    {
        _completedHistory[subscriptionIndex](this, EventArgs.Empty);
    }

    public void RaiseFailed(PlaybackErrorKind kind, string message)
    {
        RaiseCurrentFailed(new PlaybackErrorEventArgs(kind, message));
    }

    public void RaiseHistoricalFailed(int subscriptionIndex, PlaybackErrorKind kind, string message)
    {
        _failedHistory[subscriptionIndex](this, new PlaybackErrorEventArgs(kind, message));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private void RaiseCurrentFailed(PlaybackErrorEventArgs error)
    {
        foreach (var subscriber in _failedSubscribers.ToArray())
        {
            subscriber(this, error);
        }
    }
}
