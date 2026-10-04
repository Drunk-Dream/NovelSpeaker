using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Sole high-level playback state owner. Commands/results enter through the existing
/// serialized command boundary; effects execute outside this type after commit.
/// </summary>
internal sealed class PlaybackRuntime : IDisposable
{
    private readonly Guid _runtimeId = Guid.NewGuid();
    private PlaybackSessionLifetime? _lifetime;
    private long _revision;
    private bool _disposed;

    public PlaybackRuntimeState Current { get; private set; } = PlaybackRuntimeState.Idle;

    public CancellationToken SessionToken => _lifetime?.Token ?? CancellationToken.None;

    public PlaybackPreparation PrepareReplacement(PlaybackSessionTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (!IsValidTarget(target))
            return new(PlaybackTransitionRejection.InvalidTarget, null);

        var identity = new PlaybackSessionIdentity(Guid.NewGuid(), target.Book.BookId, target.Book.SourceContext);
        var state = new PlaybackRuntimeState(
            identity, target.Book, target.Position, target.Provider,
            AppSettings.NormalizeSpeakSpeed(target.SpeakSpeed), target.State,
            target.ResumePositionMilliseconds, target.ConsecutiveSegmentFailureCount,
            PlaybackAudioFacts.Empty, target.Message, target.CanRetry,
            checked(Current.ContentRevision + 1));
        return new(PlaybackTransitionRejection.None, new(_runtimeId, _revision, state));
    }

    public PlaybackTransition CommitReplacement(PlaybackReplacement replacement, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(replacement);
        if (cancellationToken.IsCancellationRequested)
            return Reject(PlaybackTransitionRejection.Cancelled);
        if (replacement.RuntimeId != _runtimeId || replacement.ExpectedRevision != _revision)
            return Reject(PlaybackTransitionRejection.StalePreparation);

        var next = replacement.State;
        // Revalidate at the commit boundary, including the resolved content/position.
        if (next.Identity is null || next.Book is null ||
            next.Identity.BookId != next.Book.BookId || next.Identity.SourceContext != next.Book.SourceContext ||
            !IsValidTarget(new(next.Book, next.Position, next.Provider, next.SpeakSpeed, next.State,
                next.ResumePositionMilliseconds, next.ConsecutiveSegmentFailureCount, next.Message, next.CanRetry)))
            return Reject(PlaybackTransitionRejection.InvalidTarget);

        var effects = new List<PlaybackEffect>();
        if (_lifetime is not null) effects.Add(new PlaybackRetireSessionEffect(_lifetime));
        AddCheckpoint(effects, next);
        if (next.State == PlaybackState.Preparing) effects.Add(new PlaybackPlaySegmentEffect(next));
        effects.Add(new PlaybackRefreshPrefetchEffect(next));

        _lifetime = new PlaybackSessionLifetime(next.Identity);
        Commit(next);
        return Accepted(effects.AsReadOnly());
    }

    public PlaybackTransition AcceptAudio(PlaybackAudioResult result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(result);
        if (!IsCurrent(result.Identity) || result.Position != Current.Position)
            return Reject(PlaybackTransitionRejection.StaleSession);
        if (result.State is not (PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Stopped or PlaybackState.Faulted) ||
            result.Audio.PositionMilliseconds < 0 || result.Audio.DurationMilliseconds < 0 ||
            (result.State is PlaybackState.Playing or PlaybackState.Paused) && !result.Audio.HasLoadedAudio ||
            (result.State is PlaybackState.Stopped or PlaybackState.Faulted) && result.Audio.HasLoadedAudio)
            return Reject(PlaybackTransitionRejection.InvalidTarget);

        Commit(Current with
        {
            State = result.State,
            Audio = result.Audio,
            ResumePositionMilliseconds = result.Audio.HasLoadedAudio
                ? result.Audio.PositionMilliseconds : Current.PositionForSave,
            ConsecutiveSegmentFailureCount = result.State == PlaybackState.Playing
                ? 0 : Current.ConsecutiveSegmentFailureCount,
            Message = result.Message,
            CanRetry = result.State == PlaybackState.Faulted
        });
        return Accepted([]);
    }

    public PlaybackTransition ChangeSpeechConfiguration(
        PlaybackSessionIdentity identity, ResolvedSpeechProvider? provider, int speakSpeed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsCurrent(identity)) return Reject(PlaybackTransitionRejection.StaleSession);
        Commit(Current with { Provider = provider, SpeakSpeed = AppSettings.NormalizeSpeakSpeed(speakSpeed) });
        // Current audio stays intact; the next segment/prefetch consumes this configuration.
        return Accepted([new PlaybackRefreshPrefetchEffect(Current)]);
    }

    public PlaybackTransition Clear(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (cancellationToken.IsCancellationRequested) return Reject(PlaybackTransitionRejection.Cancelled);
        IReadOnlyList<PlaybackEffect> effects = _lifetime is null
            ? [] : [new PlaybackRetireSessionEffect(_lifetime)];
        _lifetime = null;
        Commit(PlaybackRuntimeState.Idle with { ContentRevision = checked(Current.ContentRevision + 1) });
        return Accepted(effects);
    }

    public PlaybackTransition Checkpoint(PlaybackSessionIdentity identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsCurrent(identity)) return Reject(PlaybackTransitionRejection.StaleSession);
        var effects = new List<PlaybackEffect>();
        AddCheckpoint(effects, Current);
        return Accepted(effects.AsReadOnly());
    }

    public bool TryProtectAudio(PlaybackSessionIdentity identity, IDisposable protection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(protection);
        if (!IsCurrent(identity)) return false; // On rejection the caller still owns the handle.
        _lifetime!.ReplaceAudioProtection(protection);
        return true;
    }

    public bool IsCurrent(PlaybackSessionIdentity identity) =>
        !_disposed && identity == Current.Identity && _lifetime is not null && !_lifetime.Token.IsCancellationRequested;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _lifetime?.Dispose();
        }
        finally
        {
            _lifetime = null;
        }
    }

    private void Commit(PlaybackRuntimeState state)
    {
        var nextRevision = checked(_revision + 1);
        Current = state;
        _revision = nextRevision;
    }

    private PlaybackTransition Reject(PlaybackTransitionRejection rejection) => new(rejection, Current, []);
    private PlaybackTransition Accepted(IReadOnlyList<PlaybackEffect> effects) => new(PlaybackTransitionRejection.None, Current, effects);

    private static bool IsValidTarget(PlaybackSessionTarget target)
    {
        if (target.Book is null || string.IsNullOrWhiteSpace(target.Book.BookId) ||
            target.ResumePositionMilliseconds < 0 || target.ConsecutiveSegmentFailureCount < 0 ||
            target.State is not (PlaybackState.Preparing or PlaybackState.Paused or PlaybackState.Stopped))
            return false;
        if (target.Position is null)
            return target.State == PlaybackState.Stopped && target.ResumePositionMilliseconds == 0;
        var chapter = target.Book.Chapters.FirstOrDefault(c => c.ChapterIndex == target.Position.Value.ChapterIndex);
        var segment = target.Position.Value.SegmentIndex;
        return chapter is { LoadState: PlaybackChapterLoadState.Loaded } && segment >= 0 && segment < chapter.Segments.Count &&
            NarratableText.HasContent(chapter.Segments[segment].SpeechText) &&
            (target.State != PlaybackState.Preparing || target.Provider is not null);
    }

    private static void AddCheckpoint(List<PlaybackEffect> effects, PlaybackRuntimeState state)
    {
        if (state.Book is null || state.Position is not { } position) return;
        var chapter = state.Book.Chapters.First(c => c.ChapterIndex == position.ChapterIndex);
        effects.Add(new PlaybackCheckpointEffect(new PlaybackProgressUpdate(
            state.Book.BookId, position.ChapterIndex, position.SegmentIndex,
            chapter.Segments[position.SegmentIndex].StartOffset, state.PositionForSave, state.Book.SourceContext)));
    }
}
