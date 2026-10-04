namespace NovelSpeaker.Application.Playback;

internal enum PlaybackTransitionRejection
{
    None,
    InvalidTarget,
    StalePreparation,
    StaleSession,
    Cancelled
}

/// <summary>A preparation has no cancellation resources and does not change current state.</summary>
internal sealed class PlaybackReplacement
{
    internal PlaybackReplacement(Guid runtimeId, long expectedRevision, PlaybackRuntimeState state)
    {
        RuntimeId = runtimeId;
        ExpectedRevision = expectedRevision;
        State = state;
    }

    internal Guid RuntimeId { get; }
    internal long ExpectedRevision { get; }
    public PlaybackRuntimeState State { get; }
}

internal sealed record PlaybackPreparation(
    PlaybackTransitionRejection Rejection,
    PlaybackReplacement? Replacement)
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

// Playback-specific intents for the existing audio, progress and prefetch roles.
// Executors receive committed identity/data, not delegates or a mutable runtime.
internal abstract record PlaybackEffect;
internal sealed record PlaybackRetireSessionEffect(PlaybackSessionLifetime Lifetime) : PlaybackEffect;
internal sealed record PlaybackCheckpointEffect(PlaybackProgressUpdate Progress) : PlaybackEffect;
internal sealed record PlaybackPlaySegmentEffect(PlaybackRuntimeState Session) : PlaybackEffect;
internal sealed record PlaybackRefreshPrefetchEffect(PlaybackRuntimeState Session) : PlaybackEffect;

internal sealed record PlaybackAudioResult(
    PlaybackSessionIdentity Identity,
    PlaybackPosition Position,
    PlaybackState State,
    PlaybackAudioFacts Audio,
    string? Message = null);

/// <summary>
/// The runtime owns current session resources. After replacement the retire effect
/// owns disposal, so old work/protection is released only after the new commit.
/// </summary>
internal sealed class PlaybackSessionLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
