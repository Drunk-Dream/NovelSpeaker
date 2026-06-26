namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Holds the mutable runtime state for one isolated book playback session.
/// </summary>
public sealed class PlaybackSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private bool _disposed;

    public PlaybackSession(
        string bookId,
        int chapterIndex,
        int segmentIndex,
        long ruleId,
        string ruleName,
        int speakSpeed)
    {
        SessionId = Guid.NewGuid();
        _cancellationTokenSource = new CancellationTokenSource();
        BookId = bookId;
        ChapterIndex = chapterIndex;
        SegmentIndex = segmentIndex;
        RuleId = ruleId;
        RuleName = ruleName;
        SpeakSpeed = speakSpeed;
    }

    public Guid SessionId { get; }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public string BookId { get; }

    public int ChapterIndex { get; set; }

    public int SegmentIndex { get; set; }

    public long RuleId { get; set; }

    public string RuleName { get; set; }

    public int SpeakSpeed { get; set; }

    public long ResumePositionMilliseconds { get; set; }

    public bool HasLoadedAudio { get; set; }

    public int ConsecutiveSegmentFailureCount { get; set; }

    public void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }
}
