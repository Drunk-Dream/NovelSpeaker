namespace NovelSpeaker.Application.Playback;

internal enum PlaybackTransitionRejection
{
    None,
    InvalidTarget,
    StaleSessionReplacement,
    StaleSession,
    Cancelled
}

/// <summary>A session replacement candidate has no cancellation resources and does not change current state.</summary>
internal sealed class PlaybackSessionReplacement
{
    internal PlaybackSessionReplacement(Guid runtimeId, long expectedRevision, PlaybackRuntimeState state)
    {
        RuntimeId = runtimeId;
        ExpectedRevision = expectedRevision;
        State = state;
    }

    internal Guid RuntimeId { get; }
    internal long ExpectedRevision { get; }
    public PlaybackRuntimeState State { get; }
}

internal sealed record PlaybackSessionReplacementPreparation(
    PlaybackTransitionRejection Rejection,
    PlaybackSessionReplacement? Replacement)
{
    public bool IsAccepted => Rejection == PlaybackTransitionRejection.None;
}

internal sealed record PlaybackTransition(
    PlaybackTransitionRejection Rejection,
    PlaybackRuntimeState State,
    IReadOnlyList<PlaybackEffect> Effects)
{
    public bool IsAccepted => Rejection == PlaybackTransitionRejection.None;
}

// Playback-specific intents for the existing audio and progress roles.
// Executors receive committed identity/data, not delegates or a mutable runtime.
internal abstract record PlaybackEffect;
internal sealed record PlaybackRetireSessionEffect(PlaybackSessionLifetime Lifetime) : PlaybackEffect;
internal sealed record PlaybackCheckpointEffect(PlaybackProgressUpdate Progress) : PlaybackEffect;
internal sealed record PlaybackCancelPreparationEffect(PlaybackAudioPreparationIdentity Preparation) : PlaybackEffect;
internal sealed record PlaybackPrepareTargetAudioEffect(PlaybackRuntimeState State, PlaybackAudioPreparation Preparation) : PlaybackEffect;

internal sealed record PlaybackAudioResult(
    PlaybackSessionIdentity Identity,
    PlaybackPosition Position,
    PlaybackState State,
    PlaybackAudioFacts Audio,
    string? Message = null,
    PlaybackTargetIdentity? TargetIdentity = null,
    PlaybackAudioPreparationIdentity? PreparationIdentity = null);

// A pending target owns only cancellable preparation work, never playback truth.
internal sealed class PlaybackPreparationLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private readonly Action<PlaybackPreparationLifetime> _release;
    private bool _disposed;
    public PlaybackPreparationLifetime(
        Books.PlaybackBookContent book,
        PlaybackAudioPreparationIdentity identity,
        Action<PlaybackPreparationLifetime> release)
    {
        Book = book;
        Identity = identity;
        Token = _cancellation.Token;
        _release = release;
    }
    public Books.PlaybackBookContent Book { get; }
    public PlaybackAudioPreparationIdentity Identity { get; }
    public CancellationToken Token { get; }
    public bool IsActive { get { lock (_gate) return !_disposed; } }
    public void Cancel() { lock (_gate) { if (!_disposed) _cancellation.Cancel(); } }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cancellation.Dispose();
        }
        _release(this);
    }
}

/// <summary>Owns cancellable content resolution without changing playback state.</summary>
internal sealed class PlaybackContentWorkLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private readonly Action<PlaybackContentWorkLifetime> _release;
    private bool _disposed;

    public PlaybackContentWorkLifetime(Books.PlaybackBookContent book, Action<PlaybackContentWorkLifetime> release)
    {
        Book = book;
        _release = release;
        Token = _cancellation.Token;
    }

    public Books.PlaybackBookContent Book { get; private set; }
    public CancellationToken Token { get; }
    public bool IsActive { get { lock (_gate) return !_disposed; } }
    public void SetBook(Books.PlaybackBookContent book) { lock (_gate) Book = book; }
    public void Cancel() { lock (_gate) { if (!_disposed) _cancellation.Cancel(); } }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cancellation.Dispose();
        }
        _release(this);
    }
}

/// <summary>
/// The runtime owns current session resources. After replacement the retire effect
/// owns disposal, so old work/protection is released only after the new commit.
/// </summary>
internal sealed class PlaybackSessionLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _cancellationGate = new();
    private IDisposable? _audioProtection;
    private bool _disposed;

    public PlaybackSessionLifetime(PlaybackSessionIdentity identity)
    {
        Identity = identity;
        Token = _cancellation.Token;
    }

    public PlaybackSessionIdentity Identity { get; }
    public CancellationToken Token { get; }

    public void ReplaceAudioProtection(IDisposable? protection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_audioProtection, protection)) return;
        var previous = _audioProtection;
        _audioProtection = protection;
        previous?.Dispose();
    }

    public void Cancel()
    {
        lock (_cancellationGate) { if (!_disposed) _cancellation.Cancel(); }
    }

    public void Dispose()
    {
        lock (_cancellationGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try
        {
            _cancellation.Cancel();
        }
        finally
        {
            try
            {
                _audioProtection?.Dispose();
            }
            finally
            {
                _audioProtection = null;
                _cancellation.Dispose();
            }
        }
    }
}
