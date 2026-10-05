using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;

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
    private PlaybackPreparationLifetime? _preparation;
    private PlaybackContentWorkLifetime? _contentWork;
    private long _targetRevision;

    public PlaybackRuntimeState Current { get; private set; } = PlaybackRuntimeState.Idle;

    public CancellationToken SessionToken => _lifetime?.Token ?? CancellationToken.None;

    public PlaybackPreparationLifetime? ActivePreparation => _preparation;

    public PlaybackContentWorkLifetime? ActiveContentWork => _contentWork;

    public PlaybackContentWorkLifetime BeginContentWork(PlaybackBookContent book)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _contentWork?.Cancel();
        _contentWork = new(book, EndContentWork);
        return _contentWork;
    }

    private void EndContentWork(PlaybackContentWorkLifetime work)
    {
        if (ReferenceEquals(_contentWork, work)) _contentWork = null;
    }

    public PlaybackPreparationLifetime BeginPreparation(
        PlaybackBookContent book,
        PlaybackAudioPreparationIdentity identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _preparation?.Cancel();
        _preparation = new(book, identity, EndPreparation);
        return _preparation;
    }

    public void CancelPreparation(PlaybackAudioPreparationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (_preparation?.Identity == identity) _preparation.Cancel();
    }

    private void EndPreparation(PlaybackPreparationLifetime preparation)
    {
        if (ReferenceEquals(_preparation, preparation)) _preparation = null;
    }

    public void CancelWork(string bookId, string? sourceId)
    {
        var current = Current;
        if (current.Book?.BookId == bookId && (sourceId is null || current.Book.SourceContext?.SourceId == sourceId)) CancelSession(current.Identity);
        var preparation = _preparation;
        if (preparation?.Book.BookId == bookId &&
            (sourceId is null || preparation.Book.SourceContext is null || preparation.Book.SourceContext.SourceId == sourceId)) preparation.Cancel();
        var contentWork = _contentWork;
        if (contentWork?.Book.BookId == bookId &&
            (sourceId is null || contentWork.Book.SourceContext is null || contentWork.Book.SourceContext.SourceId == sourceId))
            contentWork.Cancel();
    }

    public void CaptureCheckpointPosition(
        PlaybackSessionIdentity identity,
        PlaybackTargetIdentity targetIdentity,
        Guid preparationAttemptId,
        long position)
    {
        if (identity != Current.Identity || targetIdentity != Current.Target?.Identity || position < 0 ||
            Current.Audio.PreparationIdentity?.AttemptId != preparationAttemptId) return;
        Commit(Current with
        {
            ResumePositionMilliseconds = position,
            Audio = Current.Audio with { PositionMilliseconds = position }
        });
    }

    public PlaybackSessionReplacementPreparation PrepareSessionReplacement(PlaybackSessionTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (!IsValidTarget(target))
            return new(PlaybackTransitionRejection.InvalidTarget, null);

        var identity = new PlaybackSessionIdentity(Guid.NewGuid(), target.Book.BookId, target.Book.SourceContext);
        var targetIdentity = target.Position is { } position
            ? new PlaybackTargetIdentity(identity.SessionId, target.Book.BookId, target.Book.SourceContext,
                checked(_targetRevision + 1))
            : null;
        var state = new PlaybackRuntimeState(
            identity, target.Book,
            targetIdentity is null ? null : new PlaybackLogicalTarget(targetIdentity, target.Position!.Value),
            target.Provider, AppSettings.NormalizeSpeakSpeed(target.SpeakSpeed), IntentFor(target.State), null, target.State,
            target.ResumePositionMilliseconds, target.ConsecutiveSegmentFailureCount,
            PlaybackAudioFacts.Empty, target.Message, target.CanRetry,
            checked(Current.ContentRevision + 1));
        if (state.Intent == PlaybackIntent.Play)
        {
            var preparationKind = target.State == PlaybackState.Recovering
                ? PlaybackPreparationKind.Recovery
                : PlaybackPreparationKind.Initial;
            state = state with { Preparation = CreatePreparation(state, preparationKind) };
        }
        return new(PlaybackTransitionRejection.None, new(_runtimeId, _revision, state));
    }

    public PlaybackTransition CommitSessionReplacement(PlaybackSessionReplacement replacement, CancellationToken cancellationToken,
        bool checkpointNewPosition = true, long? retiringPositionMilliseconds = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(replacement);
        if (cancellationToken.IsCancellationRequested)
            return Reject(PlaybackTransitionRejection.Cancelled);
        if (replacement.RuntimeId != _runtimeId || replacement.ExpectedRevision != _revision)
            return Reject(PlaybackTransitionRejection.StaleSessionReplacement);

        var next = replacement.State;
        // Revalidate at the commit boundary, including the resolved content/position.
        if (next.Identity is null || next.Book is null ||
            next.Identity.BookId != next.Book.BookId || next.Identity.SourceContext != next.Book.SourceContext ||
            next.Target is { } logicalTarget && (logicalTarget.Identity.SessionId != next.Identity.SessionId ||
                logicalTarget.Identity.BookId != next.Identity.BookId || logicalTarget.Identity.SourceContext != next.Identity.SourceContext) ||
            !IsValidTarget(new(next.Book, next.Position, next.Provider, next.SpeakSpeed, next.State,
                next.ResumePositionMilliseconds, next.ConsecutiveSegmentFailureCount, next.Message, next.CanRetry)))
            return Reject(PlaybackTransitionRejection.InvalidTarget);

        var effects = new List<PlaybackEffect>();
        if (Current.Preparation is { } obsoletePreparation)
            effects.Add(new PlaybackCancelPreparationEffect(obsoletePreparation.Identity));
        if (_lifetime is not null)
        {
            var previous = retiringPositionMilliseconds is { } position
                ? Current with
                {
                    ResumePositionMilliseconds = Math.Max(0, position),
                    Audio = Current.Audio with { PositionMilliseconds = Math.Max(0, position) }
                }
                : Current;
            AddCheckpoint(effects, previous);
        }
        if (_lifetime is not null) effects.Add(new PlaybackRetireSessionEffect(_lifetime));
        if (checkpointNewPosition) AddCheckpoint(effects, next);
        if (next.Preparation is { } preparation)
            effects.Add(new PlaybackPrepareTargetAudioEffect(next, preparation));
        _lifetime = new PlaybackSessionLifetime(next.Identity);
        Commit(next);
        return Accepted(effects.AsReadOnly());
    }

    public PlaybackTransition AcceptAudio(PlaybackAudioResult result, bool authoritativeTransportChange = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(result);
        if (!IsCurrent(result.Identity) || result.Position != Current.Position ||
            Current.Target is not { } target || result.TargetIdentity != target.Identity ||
            result.PreparationIdentity is not { } preparation ||
            preparation.Session != result.Identity || preparation.Target != target.Identity ||
            (Current.Preparation?.Identity != preparation && Current.Audio.PreparationIdentity != preparation) ||
            (Current.Preparation?.Identity == preparation && !IsCurrentPreparation(preparation)))
            return Reject(PlaybackTransitionRejection.StaleSession);
        if (result.State is not (PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Stopped or PlaybackState.Faulted) ||
            result.Audio.PositionMilliseconds < 0 || result.Audio.DurationMilliseconds < 0 ||
            (result.State is PlaybackState.Playing or PlaybackState.Paused) && !result.Audio.HasLoadedAudio ||
            (result.State is PlaybackState.Stopped or PlaybackState.Faulted) && result.Audio.HasLoadedAudio)
            return Reject(PlaybackTransitionRejection.InvalidTarget);
        if (!authoritativeTransportChange &&
            ((Current.State == PlaybackState.Playing && Current.Intent == PlaybackIntent.Play && result.State == PlaybackState.Paused) ||
            (Current.State == PlaybackState.Paused && Current.Intent == PlaybackIntent.Pause && result.State == PlaybackState.Playing))
           )
            return Reject(PlaybackTransitionRejection.StaleSession);

        Commit(Current with
        {
            State = result.State,
            Preparation = result.State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Stopped or PlaybackState.Faulted
                ? null : Current.Preparation,
            Audio = result.Audio with
            {
                TargetIdentity = target.Identity,
                PreparationIdentity = preparation
            },
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
        PlaybackSessionIdentity? identity, ResolvedSpeechProvider? provider, int speakSpeed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (identity is null ? Current.Identity is not null : !IsCurrent(identity))
            return Reject(PlaybackTransitionRejection.StaleSession);
        var next = Current with { Provider = provider, SpeakSpeed = AppSettings.NormalizeSpeakSpeed(speakSpeed) };
        if (Current.Intent == PlaybackIntent.Play && !Current.Audio.HasLoadedAudio)
        {
            var previousSynthesis = Current.Preparation?.Identity.Synthesis;
            var nextSynthesis = CreateSynthesisIdentity(next);
            var previousProviderId = Current.Provider?.ProviderId;
            var nextProviderId = provider?.ProviderId;
            if (!Equals(previousSynthesis, nextSynthesis) || previousProviderId != nextProviderId ||
                Current.Preparation is null && nextSynthesis is not null)
            {
                var kind = Current.Preparation?.Kind ?? PlaybackPreparationKind.Initial;
                next = next with
                {
                    Preparation = CreatePreparation(next, kind),
                    State = provider is null ? PlaybackState.Stopped : PlaybackState.Preparing,
                    Message = provider is null ? null : "语音配置已更新，正在准备当前段音频。"
                };
            }
        }
        Commit(next);
        // Current audio stays intact; the next segment/prefetch consumes this configuration.
        return Accepted([]);
    }

    public PlaybackTransition Clear(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (cancellationToken.IsCancellationRequested) return Reject(PlaybackTransitionRejection.Cancelled);
        var effects = new List<PlaybackEffect>();
        if (Current.Preparation is { } preparation)
            effects.Add(new PlaybackCancelPreparationEffect(preparation.Identity));
        if (_lifetime is not null) effects.Add(new PlaybackRetireSessionEffect(_lifetime));
        _lifetime = null;
        Commit(PlaybackRuntimeState.Idle with { ContentRevision = checked(Current.ContentRevision + 1) });
        return Accepted(effects.AsReadOnly());
    }

    // Cancellation may be signalled by a committed Source change while a serialized
    // command is awaiting content. It invalidates work, never mutates the runtime read.
    public void CancelSession(PlaybackSessionIdentity? identity)
    {
        // Capture the resource owner, then compare its immutable identity. Replacement
        // may publish a new lifetime concurrently, but this call can only cancel the
        // captured matching owner, never whichever owner is current later.
        var lifetime = _lifetime;
        if (identity is not null && lifetime?.Identity == identity) lifetime.Cancel();
        var preparation = _preparation;
        if (identity is not null && preparation?.Identity.Session == identity) preparation.Cancel();
    }

    public PlaybackTransition Stop(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var effects = new List<PlaybackEffect>();
        if (Current.Preparation is { } preparation)
            effects.Add(new PlaybackCancelPreparationEffect(preparation.Identity));
        if (_lifetime is not null) AddCheckpoint(effects, Current);
        if (_lifetime is not null) effects.Add(new PlaybackRetireSessionEffect(_lifetime));
        _lifetime = null;
        Commit(Current with
        {
            Identity = null,
            Intent = PlaybackIntent.Stop,
            Preparation = null,
            State = PlaybackState.Stopped,
            ResumePositionMilliseconds = Current.PositionForSave,
            Audio = PlaybackAudioFacts.Empty,
            Message = message,
            CanRetry = false
        });
        return Accepted(effects.AsReadOnly());
    }

    public PlaybackTransition Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Current.Identity is not { } identity || !IsCurrent(identity)) return Reject(PlaybackTransitionRejection.StaleSession);
        var activePreparation = Current.Preparation;
        Commit(Current with
        {
            Intent = PlaybackIntent.Pause,
            Preparation = null,
            State = PlaybackState.Paused,
            Message = "已暂停，等待播放。"
        });
        var effects = new List<PlaybackEffect>();
        if (activePreparation is not null) effects.Add(new PlaybackCancelPreparationEffect(activePreparation.Identity));
        AddCheckpoint(effects, Current);
        return Accepted(effects.AsReadOnly());
    }

    public PlaybackTransition ResumeLoadedAudio()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Current.Identity is not { } identity || !IsCurrent(identity) || !Current.Audio.HasLoadedAudio)
            return Reject(PlaybackTransitionRejection.StaleSession);

        Commit(Current with
        {
            Intent = PlaybackIntent.Play,
            State = PlaybackState.Playing,
            Message = null,
            CanRetry = false
        });
        return Accepted([]);
    }

    public PlaybackTransition BeginPlayback(bool resetFailureWindow, bool recovering = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Current.Identity is not { } identity || !IsCurrent(identity) || Current.Provider is null)
            return Reject(PlaybackTransitionRejection.StaleSession);
        var next = Current with
        {
            Intent = PlaybackIntent.Play,
            Preparation = null,
            State = recovering ? PlaybackState.Recovering : PlaybackState.Preparing,
            Audio = PlaybackAudioFacts.Empty,
            CanRetry = false,
            ConsecutiveSegmentFailureCount = resetFailureWindow ? 0 : Current.ConsecutiveSegmentFailureCount,
            LastFailureKind = resetFailureWindow ? null : Current.LastFailureKind,
            Message = recovering ? "检测到音频损坏，正在重新生成。" : "正在准备当前段音频。"
        };
        var preparationKind = recovering
            ? PlaybackPreparationKind.Recovery
            : PlaybackPreparationKind.Initial;
        next = next with { Preparation = CreatePreparation(next, preparationKind) };
        Commit(next);
        return Accepted(Current.Preparation is { } preparation
            ? [new PlaybackPrepareTargetAudioEffect(Current, preparation)]
            : []);
    }

    /// <summary>
    /// Commits a same-session logical target before any audio work. This transition is
    /// intentionally independent of cache, synthesis and local-player readiness.
    /// </summary>
    public PlaybackTransition CommitTarget(
        PlaybackBookContent book,
        PlaybackPosition position,
        PlaybackIntent intent,
        CancellationToken cancellationToken,
        string? message = null,
        long resumePositionMilliseconds = 0,
        bool canRetry = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(book);
        if (cancellationToken.IsCancellationRequested) return Reject(PlaybackTransitionRejection.Cancelled);
        if (Current.Identity is not { } session || !IsCurrent(session) ||
            book.BookId != session.BookId || book.SourceContext != session.SourceContext ||
            resumePositionMilliseconds < 0 ||
            !IsValidTarget(new(book, position, Current.Provider, Current.SpeakSpeed, PlaybackState.Paused)))
            return Reject(PlaybackTransitionRejection.InvalidTarget);

        var previous = Current;
        var targetIdentity = new PlaybackTargetIdentity(session.SessionId, book.BookId, book.SourceContext,
            checked(_targetRevision + 1));
        var next = Current with
        {
            Book = book,
            Target = new PlaybackLogicalTarget(targetIdentity, position),
            Intent = intent,
            Preparation = null,
            State = intent switch
            {
                PlaybackIntent.Play when Current.Provider is not null => PlaybackState.Preparing,
                PlaybackIntent.Pause => PlaybackState.Paused,
                _ => PlaybackState.Stopped
            },
            ResumePositionMilliseconds = resumePositionMilliseconds,
            Audio = PlaybackAudioFacts.Empty,
            Message = message,
            CanRetry = canRetry,
            ContentRevision = ReferenceEquals(book, Current.Book)
                ? Current.ContentRevision
                : checked(Current.ContentRevision + 1)
        };
        if (intent == PlaybackIntent.Play && next.Provider is not null)
            next = next with { Preparation = CreatePreparation(next, PlaybackPreparationKind.Initial) };

        var effects = new List<PlaybackEffect>();
        AddCheckpoint(effects, previous);
        AddCheckpoint(effects, next);
        if (previous.Preparation is { } obsolete)
            effects.Add(new PlaybackCancelPreparationEffect(obsolete.Identity));
        if (next.Preparation is { } preparation)
            effects.Add(new PlaybackPrepareTargetAudioEffect(next, preparation));
        Commit(next);
        return Accepted(effects.AsReadOnly());
    }

    public bool IsCurrentPreparation(PlaybackAudioPreparationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (_disposed || Current.Preparation is not { } current || current.Identity != identity ||
            Current.Intent != PlaybackIntent.Play ||
            !IsCurrent(identity.Session) || Current.Target?.Identity != identity.Target)
            return false;
        return CreateSynthesisIdentity(Current)?.Equals(identity.Synthesis) == true;
    }

    public PlaybackTransition UpdateContent(PlaybackBookContent book, PlaybackPosition? position, string? message = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (book.BookId != Current.Book?.BookId || book.SourceContext != Current.Book.SourceContext ||
            position is { } p && !IsValidTarget(new(book, p, Current.Provider, Current.SpeakSpeed, PlaybackState.Paused)) ||
            position is not null && Current.Identity is null && Current.Target is null)
            return Reject(PlaybackTransitionRejection.InvalidTarget);
        var previous = Current;
        var targetChanged = position != Current.Position;
        var sessionId = Current.Identity?.SessionId ?? Current.Target?.Identity.SessionId ?? Guid.NewGuid();
        var target = position is { } resolved
            ? new PlaybackLogicalTarget(targetChanged
                ? new PlaybackTargetIdentity(sessionId, book.BookId, book.SourceContext, checked(_targetRevision + 1))
                : Current.Target!.Identity, resolved)
            : null;
        var next = Current with
        {
            Book = book,
            Target = target,
            Preparation = targetChanged ? null : Current.Preparation,
            Intent = targetChanged && position is null ? PlaybackIntent.Stop : Current.Intent,
            State = targetChanged
                ? position is null ? PlaybackState.Stopped : Current.Intent switch
                {
                    PlaybackIntent.Play when Current.Provider is not null => PlaybackState.Preparing,
                    PlaybackIntent.Pause => PlaybackState.Paused,
                    _ => PlaybackState.Stopped
                }
                : Current.State,
            ResumePositionMilliseconds = targetChanged ? 0 : Current.ResumePositionMilliseconds,
            Audio = targetChanged ? PlaybackAudioFacts.Empty : Current.Audio,
            ContentRevision = checked(Current.ContentRevision + 1),
            Message = message ?? Current.Message
        };
        if (targetChanged && next.Intent == PlaybackIntent.Play && next.Provider is not null)
            next = next with { Preparation = CreatePreparation(next, PlaybackPreparationKind.Initial) };

        var effects = new List<PlaybackEffect>();
        if (targetChanged)
        {
            AddCheckpoint(effects, previous);
            if (next.Position is not null) AddCheckpoint(effects, next);
            if (previous.Preparation is { } obsolete)
                effects.Add(new PlaybackCancelPreparationEffect(obsolete.Identity));
            if (next.Preparation is { } preparation)
                effects.Add(new PlaybackPrepareTargetAudioEffect(next, preparation));
        }
        Commit(next);
        return Accepted(effects.AsReadOnly());
    }

    public PlaybackTransition UpdateMetadata(PlaybackBookContent metadata)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Current.Book is not { } book || book.BookId != metadata.BookId || book.SourceContext != metadata.SourceContext)
            return Reject(PlaybackTransitionRejection.InvalidTarget);
        Commit(Current with { Book = book with { BookTitle = metadata.BookTitle, BookAuthor = metadata.BookAuthor } });
        return Accepted([]);
    }

    public PlaybackRecoveryDecision RecordFailure(PlaybackRecoveryPolicy policy, TtsErrorKind kind, string message, bool corruptAudio)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = $"{Current.Identity?.SessionId:N}:{Current.Position}:{Current.Provider?.ProviderId}:{Current.SpeakSpeed}";
        var decision = policy.Decide(new(kind, message, Current.ConsecutiveSegmentFailureCount,
            corruptAudio, Current.RecoveredCorruptSegmentKey == key));
        Commit(Current with
        {
            LastFailureKind = kind,
            ConsecutiveSegmentFailureCount = decision.ConsecutiveSegmentFailureCount,
            RecoveredCorruptSegmentKey = decision.ShouldRetryCurrentSegment ? key : Current.RecoveredCorruptSegmentKey
        });
        return decision;
    }

    public void ReportMessage(string message) => Commit(Current with { Message = message });

    public void ReportFailure(string message) => Commit(Current with
    {
        State = PlaybackState.Faulted,
        Message = message,
        CanRetry = Current.Book is not null,
        LastFailureKind = TtsErrorKind.Unknown
    });

    public void ChangeIdleSpeed(int speakSpeed) => Commit(Current with { SpeakSpeed = AppSettings.NormalizeSpeakSpeed(speakSpeed) });

    public void ResetFailureWindow() => Commit(Current with { ConsecutiveSegmentFailureCount = 0, LastFailureKind = null });

    public PlaybackTransition Checkpoint(PlaybackSessionIdentity identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsCurrent(identity)) return Reject(PlaybackTransitionRejection.StaleSession);
        var effects = new List<PlaybackEffect>();
        AddCheckpoint(effects, Current);
        return Accepted(effects.AsReadOnly());
    }

    public bool TryProtectAudio(PlaybackSessionIdentity identity, IDisposable? protection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
            _preparation?.Cancel();
            _contentWork?.Cancel();
            _lifetime?.Dispose();
        }
        finally
        {
            _lifetime = null;
            _contentWork = null;
        }
    }

    private void Commit(PlaybackRuntimeState state)
    {
        var nextRevision = checked(_revision + 1);
        Current = state;
        if (state.Target is { } target && target.Identity.Revision > _targetRevision)
            _targetRevision = target.Identity.Revision;
        _revision = nextRevision;
    }

    private static PlaybackIntent IntentFor(PlaybackState state) => state switch
    {
        PlaybackState.Preparing or PlaybackState.Recovering or PlaybackState.Playing => PlaybackIntent.Play,
        PlaybackState.Paused => PlaybackIntent.Pause,
        _ => PlaybackIntent.Stop
    };

    private static PlaybackAudioPreparation? CreatePreparation(
        PlaybackRuntimeState state,
        PlaybackPreparationKind kind)
    {
        if (state.Identity is not { } session || state.Target is not { } target ||
            state.Book is null || state.Provider is null || state.Intent != PlaybackIntent.Play)
            return null;

        var synthesis = CreateSynthesisIdentity(state);
        return synthesis is null ? null : new(
            new(session, target.Identity, synthesis, Guid.NewGuid()), kind);
    }

    private static AudioCacheIdentity? CreateSynthesisIdentity(PlaybackRuntimeState state)
    {
        if (state.Target is not { } target || state.Book is not { } book || state.Provider is not { } provider)
            return null;
        var chapter = book.Chapters.FirstOrDefault(value => value.ChapterIndex == target.Position.ChapterIndex);
        if (chapter is null || target.Position.SegmentIndex < 0 || target.Position.SegmentIndex >= chapter.Segments.Count)
            return null;
        var segment = chapter.Segments[target.Position.SegmentIndex];
        return AudioCacheIdentity.Create(
            chapter.ChapterId ?? $"{book.BookId}/chapter/{chapter.ChapterIndex}",
            segment.StableIdentity,
            segment.SpeechText,
            SynthesisProfileFingerprint.Create(ProviderSynthesisFingerprint.Create(provider.Provider), state.SpeakSpeed));
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
