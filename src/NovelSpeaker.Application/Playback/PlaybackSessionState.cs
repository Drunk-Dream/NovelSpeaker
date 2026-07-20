namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Owns all mutable resources and playback data belonging to one playback session.
/// </summary>
internal sealed class PlaybackSessionState : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private IDisposable? _audioProtection;
    private bool _disposed;

    public PlaybackSessionState(
        PlaybackBookContent book,
        int chapterIndex,
        int segmentIndex,
        SelectedPlaybackRule? rule,
        int speakSpeed)
    {
        ArgumentNullException.ThrowIfNull(book);
        SessionId = Guid.NewGuid();
        Book = book;
        ChapterIndex = chapterIndex;
        SegmentIndex = segmentIndex;
        Rule = rule;
        SpeakSpeed = speakSpeed;
    }

    public Guid SessionId { get; }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public PlaybackBookContent Book { get; set; }

    public string BookId => Book.BookId;

    public SelectedPlaybackRule? Rule { get; set; }

    public long RuleId => Rule?.RuleId ?? 0;

    public string RuleName => Rule?.RuleName ?? string.Empty;

    public void SetRule(SelectedPlaybackRule? rule)
    {
        Rule = rule;
    }

    public int ChapterIndex { get; set; }

    public int SegmentIndex { get; set; }

    public int SpeakSpeed { get; set; }

    public long ResumePositionMilliseconds { get; set; }

    public int ConsecutiveSegmentFailureCount { get; set; }

    public LocalAudioPlaybackSnapshot CurrentAudio { get; private set; } = LocalAudioPlaybackSnapshot.Idle;

    public bool HasLoadedAudio => CurrentAudio.State is not (PlaybackState.Idle or PlaybackState.Stopped or PlaybackState.Faulted);

    public IDisposable? AudioProtection => _audioProtection;

    public long PositionForSave => HasLoadedAudio
        ? CurrentAudio.PositionMilliseconds
        : ResumePositionMilliseconds;

    public void UpdateAudio(LocalAudioPlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CurrentAudio = snapshot;
        ResumePositionMilliseconds = snapshot.PositionMilliseconds;
    }

    public void SetPositionForSave(long positionMilliseconds)
    {
        ResumePositionMilliseconds = positionMilliseconds;
        if (HasLoadedAudio)
        {
            CurrentAudio = CurrentAudio with { PositionMilliseconds = positionMilliseconds };
        }
    }

    public void ReplaceAudioProtection(IDisposable? protection)
    {
        _audioProtection?.Dispose();
        _audioProtection = protection;
    }

    public void Cancel()
    {
        if (!_disposed)
        {
            _cancellationTokenSource.Cancel();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _cancellationTokenSource.Cancel();
        _audioProtection?.Dispose();
        _audioProtection = null;
        _cancellationTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }
}
